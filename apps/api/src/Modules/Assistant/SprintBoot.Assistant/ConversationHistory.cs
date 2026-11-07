using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SprintBoot.Assistant;

/// <summary>How an issue goes with a question: tagged with a version, so the same content is sent only once.</summary>
public static partial class IssueContext
{
    public static string Block(string key, string content)
    {
        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12];
        return $"<issue key=\"{key}\" version=\"{version}\">\n{content}\n</issue>";
    }

    /// <summary>The opening tag, which finds earlier questions that already carried this exact content.</summary>
    public static string Marker(string block) => block[..(block.IndexOf('>') + 1)];

    public static string Unchanged(string block)
    {
        var match = Opening().Match(block);
        return $"<issue key=\"{match.Groups[1].Value}\" version=\"{match.Groups[2].Value}\" unchanged=\"true\">The content is the same as the earlier copy with this version above.</issue>";
    }

    /// <summary>Full copies in a text, by version.</summary>
    public static IEnumerable<(string Version, string Block)> Copies(string text) =>
        Full().Matches(text).Select(match => (match.Groups[2].Value, match.Value));

    /// <summary>The versions a text refers to without carrying them.</summary>
    public static IEnumerable<(string Version, string Reference)> References(string text) =>
        Reference().Matches(text).Select(match => (match.Groups[2].Value, match.Value));

    [GeneratedRegex("""^<issue key="([^"]+)" version="([0-9a-f]+)">""")]
    private static partial Regex Opening();

    [GeneratedRegex("""<issue key="([^"]+)" version="([0-9a-f]+)">\n.*?\n</issue>""", RegexOptions.Singleline)]
    private static partial Regex Full();

    [GeneratedRegex("""<issue key="([^"]+)" version="([0-9a-f]+)" unchanged="true">.*?</issue>""", RegexOptions.Singleline)]
    private static partial Regex Reference();
}

/// <summary>Which turns of a conversation go to the provider when it takes only so much.</summary>
public static class ConversationHistory
{
    public const string LeftOut = "(Earlier messages of this conversation were left out to fit the provider's size limit.)";

    /// <summary>
    /// Everything when it fits. Otherwise the first exchange and as many of the latest as fit, whole: a question with every
    /// step and tool result after it, so no tool call ever loses its result. The latest exchange always goes, even alone
    /// over the budget. Issue copies that only a left-out turn carried are put back where they are referred to.
    /// </summary>
    public static List<ConversationMessage> Fit(List<ConversationMessage> history, int? budget)
    {
        if (budget is not { } limit || Weight(history) <= limit)
        {
            return history;
        }

        var exchanges = new List<List<ConversationMessage>>();
        foreach (var message in history)
        {
            if (message.Role == ConversationRole.User || exchanges.Count == 0)
            {
                exchanges.Add([]);
            }

            exchanges[^1].Add(message);
        }

        // The first exchange says what the conversation is about, so it is held before the latest ones fill the rest.
        var kept = new List<List<ConversationMessage>> { exchanges[^1] };
        var used = Weight(exchanges[^1]);
        var keepFirst = exchanges.Count > 1 && used + Weight(exchanges[0]) <= limit;
        if (keepFirst)
        {
            used += Weight(exchanges[0]);
        }

        for (var index = exchanges.Count - 2; index >= 1; index--)
        {
            var weight = Weight(exchanges[index]);
            if (used + weight > limit)
            {
                break;
            }

            kept.Insert(0, exchanges[index]);
            used += weight;
        }

        if (keepFirst)
        {
            kept.Insert(0, exchanges[0]);
        }

        var result = kept.SelectMany(exchange => exchange).ToList();
        var dropped = history.Except(result).ToList();
        if (dropped.Count == 0)
        {
            return result;
        }

        return Restore(result, dropped);
    }

    /// <summary>Marks the first kept question after the gap, and gives back issue copies only left-out turns had.</summary>
    private static List<ConversationMessage> Restore(List<ConversationMessage> kept, List<ConversationMessage> dropped)
    {
        var copies = new Dictionary<string, string>();
        foreach (var message in dropped.Where(item => item.Role == ConversationRole.User && item.Prompt is not null))
        {
            foreach (var (version, block) in IssueContext.Copies(message.Prompt!))
            {
                copies[version] = block;
            }
        }

        var present = kept
            .Where(item => item.Role == ConversationRole.User && item.Prompt is not null)
            .SelectMany(item => IssueContext.Copies(item.Prompt!).Select(copy => copy.Version))
            .ToHashSet();
        var firstDropped = dropped.Min(item => item.Sequence);
        var marked = false;
        var result = new List<ConversationMessage>();
        foreach (var message in kept)
        {
            if (message.Role != ConversationRole.User)
            {
                result.Add(message);
                continue;
            }

            var prompt = message.Prompt ?? message.Text;
            foreach (var (version, reference) in IssueContext.References(prompt).ToList())
            {
                if (!present.Contains(version) && copies.TryGetValue(version, out var block))
                {
                    prompt = prompt.Replace(reference, block, StringComparison.Ordinal);
                    present.Add(version);
                }
            }

            if (!marked && message.Sequence > firstDropped)
            {
                prompt = $"{LeftOut}\n\n{prompt}";
                marked = true;
            }

            result.Add(prompt == (message.Prompt ?? message.Text) ? message : message.WithPrompt(prompt));
        }

        return result;
    }

    /// <summary>Roughly what the turns weigh in the request body, in bytes.</summary>
    internal static int Weight(IEnumerable<ConversationMessage> messages) => messages.Sum(message =>
        Encoding.UTF8.GetByteCount(message.Prompt ?? message.Text)
        + Encoding.UTF8.GetByteCount(message.ToolsJson ?? string.Empty)
        + Encoding.UTF8.GetByteCount(message.NativeJson ?? string.Empty)
        + 64);
}
