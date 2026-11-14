using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SprintBoot.Assistant;

/// <summary>
/// How a ticket goes with a question. The first question carries the whole ticket; later ones carry a one-line header when
/// nothing changed, or only the fields and comments that changed since the copy the model already has. Every block names
/// the version of the ticket it describes, so the history can be read back into the ticket as the model knows it.
/// </summary>
public static partial class TicketContext
{
    private const string Comments = "comments";
    private const string NewComments = "newComments";
    private const string EditedComments = "editedComments";
    private const string RemovedComments = "removedComments";

    private static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A block of one ticket in a question, as the history stores it.</summary>
    public sealed record Mention(string Key, string Version, MentionKind Kind, string? From, string Body, string Text);

    public enum MentionKind
    {
        Full,
        Unchanged,
        Changes,
    }

    public static string Version(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12];

    /// <summary>The whole ticket.</summary>
    public static string Block(string key, string content) => Full(key, Version(content), content);

    /// <summary>
    /// What a question carries about a ticket, given the questions before it: the whole ticket the first time, a header
    /// when it is the same, or what changed since the copy the model has.
    /// </summary>
    public static string ForQuestion(string key, string content, IEnumerable<string> earlierPrompts)
    {
        var version = Version(content);
        var now = Normalize(content);
        var known = Known(earlierPrompts);
        if (!known.Latest.TryGetValue(key, out var latest))
        {
            return Full(key, version, content);
        }

        if (latest == version)
        {
            return Unchanged(key, version, now);
        }

        // Content the model cannot compare, such as a clipped one, goes whole.
        if (now is null || known.States.GetValueOrDefault(latest) is not { } before)
        {
            return Full(key, version, content);
        }

        return Diff(before, now) is { } changes ? Changed(key, version, latest, changes) : Unchanged(key, version, now);
    }

    /// <summary>Every ticket block in a text, in order.</summary>
    public static IEnumerable<Mention> Mentions(string text) => Any().Matches(text).Select(match => new Mention(
        match.Groups["key"].Value,
        match.Groups["version"].Value,
        match.Groups["unchanged"].Success ? MentionKind.Unchanged : match.Groups["from"].Success ? MentionKind.Changes : MentionKind.Full,
        match.Groups["from"].Success ? match.Groups["from"].Value : null,
        match.Groups["body"].Value.Trim('\n'),
        match.Value));

    /// <summary>
    /// The tickets as the model knows them after the given prompts: the state of each version, and the latest version of
    /// each ticket. A state is null when the copy was no JSON, so nothing can be worked out from it.
    /// </summary>
    public static (Dictionary<string, JsonObject?> States, Dictionary<string, string> Latest) Known(IEnumerable<string> prompts)
    {
        var states = new Dictionary<string, JsonObject?>();
        var latest = new Dictionary<string, string>();
        foreach (var mention in prompts.SelectMany(Mentions))
        {
            var current = latest.TryGetValue(mention.Key, out var previous) ? states.GetValueOrDefault(previous) : null;
            switch (mention.Kind)
            {
                case MentionKind.Full:
                    states[mention.Version] = Normalize(mention.Body);
                    break;
                case MentionKind.Changes:
                    var origin = mention.From is not null && states.TryGetValue(mention.From, out var from) ? from : current;
                    states[mention.Version] = origin is not null && Parse(ChangesJson(mention.Body)) is { } changes ? Apply(origin, changes) : null;
                    break;
                default:
                    states.TryAdd(mention.Version, current);
                    break;
            }

            latest[mention.Key] = mention.Version;
        }

        return (states, latest);
    }

    /// <summary>A whole copy of a ticket worked out from the history, to stand in for a header whose copy was left out.</summary>
    public static string FullFromState(string key, string version, JsonObject state) => Full(key, version, state.ToJsonString(Compact));

    /// <summary>The fields and comments that differ, or null when nothing does. Both sides are normalized.</summary>
    public static JsonObject? Diff(JsonObject before, JsonObject now)
    {
        var changes = new JsonObject();
        foreach (var name in before.Select(pair => pair.Key).Union(now.Select(pair => pair.Key)).Where(name => name != Comments))
        {
            if (!JsonNode.DeepEquals(before[name], now[name]))
            {
                changes[name] = now[name]?.DeepClone();
            }
        }

        var old = CommentsById(before);
        var current = CommentsById(now);
        var added = current.Where(pair => !old.ContainsKey(pair.Key)).Select(pair => pair.Value.DeepClone()).ToArray();
        var edited = current.Where(pair => old.TryGetValue(pair.Key, out var was) && !JsonNode.DeepEquals(was, pair.Value)).Select(pair => pair.Value.DeepClone()).ToArray();
        var removed = old.Keys.Where(id => !current.ContainsKey(id)).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray();
        if (added.Length > 0)
        {
            changes[NewComments] = new JsonArray(added);
        }

        if (edited.Length > 0)
        {
            changes[EditedComments] = new JsonArray(edited);
        }

        if (removed.Length > 0)
        {
            changes[RemovedComments] = new JsonArray(removed);
        }

        return changes.Count == 0 ? null : changes;
    }

    /// <summary>The ticket after the changes <see cref="Diff"/> found.</summary>
    public static JsonObject Apply(JsonObject before, JsonObject changes)
    {
        var after = before.DeepClone().AsObject();
        foreach (var (name, value) in changes)
        {
            if (name is NewComments or EditedComments or RemovedComments)
            {
                continue;
            }

            if (value is null)
            {
                after.Remove(name);
            }
            else
            {
                after[name] = value.DeepClone();
            }
        }

        var list = after[Comments] as JsonArray ?? [];
        var removed = (changes[RemovedComments] as JsonArray ?? []).Select(id => id?.GetValue<string>()).ToHashSet();
        var edited = (changes[EditedComments] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(comment => IdOf(comment) ?? string.Empty);
        var comments = list.OfType<JsonObject>()
            .Where(comment => !removed.Contains(IdOf(comment)))
            .Select(comment => (JsonNode)(edited.TryGetValue(IdOf(comment) ?? string.Empty, out var replaced) ? replaced : comment).DeepClone())
            .Concat((changes[NewComments] as JsonArray ?? []).Select(comment => comment!.DeepClone()))
            .ToArray();
        if (comments.Length > 0 || after.ContainsKey(Comments))
        {
            after[Comments] = new JsonArray(comments);
        }

        return after;
    }

    /// <summary>
    /// The ticket as JSON with its comments in one flat list, replies naming their parent, so comments compare by id. Null
    /// when the content is no JSON object.
    /// </summary>
    public static JsonObject? Normalize(string content)
    {
        if (Parse(content) is not { } ticket)
        {
            return null;
        }

        if (ticket[Comments] is JsonArray thread)
        {
            var flat = new JsonArray();
            void Add(JsonArray items, string? parent)
            {
                foreach (var comment in items.OfType<JsonObject>())
                {
                    var copy = comment.DeepClone().AsObject();
                    var replies = copy["replies"] as JsonArray;
                    copy.Remove("replies");
                    if (parent is not null)
                    {
                        copy["replyTo"] = parent;
                    }

                    flat.Add(copy);
                    if (replies is not null)
                    {
                        Add(replies, IdOf(comment));
                    }
                }
            }

            Add(thread, null);
            ticket[Comments] = flat;
        }

        return ticket;
    }

    private static JsonObject? Parse(string content)
    {
        try
        {
            return JsonNode.Parse(content) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The changes follow a line that says what they are.
    private static string ChangesJson(string body) => body.IndexOf('{') is var start and >= 0 ? body[start..] : body;

    private static Dictionary<string, JsonObject> CommentsById(JsonObject ticket)
    {
        var map = new Dictionary<string, JsonObject>();
        foreach (var comment in (ticket[Comments] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (IdOf(comment) is { } id)
            {
                map.TryAdd(id, comment);
            }
        }

        return map;
    }

    private static string? IdOf(JsonObject comment) => comment["id"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;

    private static string Full(string key, string version, string content) => $"<ticket key=\"{key}\" version=\"{version}\">\n{content}\n</ticket>";

    private static string Unchanged(string key, string version, JsonObject? ticket)
    {
        string Field(string name) => ticket?[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : "none";
        var header = ticket is null
            ? key
            : $"{key} · {Field("title")} · status {Field("status")} · assignee {Field("assignee")} · sprint {Field("sprint")}";
        return $"<ticket key=\"{key}\" version=\"{version}\" unchanged=\"true\">{header}. Unchanged since the copy with this version above.</ticket>";
    }

    private static string Changed(string key, string version, string from, JsonObject changes) =>
        $"<ticket key=\"{key}\" version=\"{version}\" changes-from=\"{from}\">\nOnly what changed since version {from}; null means emptied.\n{changes.ToJsonString(Compact)}\n</ticket>";

    [GeneratedRegex("""<ticket key="(?<key>[^"]+)" version="(?<version>[0-9a-f]+)"(?:(?<unchanged> unchanged="true")| changes-from="(?<from>[0-9a-f]+)")?>(?<body>.*?)</ticket>""", RegexOptions.Singleline)]
    private static partial Regex Any();
}
