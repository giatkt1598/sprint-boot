using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SprintBoot.Assistant;

/// <summary>Which turns of a conversation go to the provider when it takes only so much.</summary>
public static class ConversationHistory
{
    public const string LeftOut = "(Earlier messages of this conversation were left out to fit the provider's size limit.)";

    /// <summary>
    /// Everything when it fits. Otherwise the first exchange and as many of the latest as fit, whole: a question with every
    /// step and tool result after it, so no tool call ever loses its result. The latest exchange always goes, even alone
    /// over the budget. Ticket copies that only a left-out turn carried are put back where they are referred to.
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

        return Restore(history, result, dropped);
    }

    /// <summary>
    /// Marks the first kept question after the gap. A ticket whose whole copy only a left-out turn carried comes back whole
    /// where the kept turns first mention it, worked out from everything the history said about it.
    /// </summary>
    private static List<ConversationMessage> Restore(List<ConversationMessage> history, List<ConversationMessage> kept, List<ConversationMessage> dropped)
    {
        var known = TicketContext.Known(history.Where(item => item.Role == ConversationRole.User).Select(item => item.Prompt ?? item.Text));
        var whole = new HashSet<string>();
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
            foreach (var mention in TicketContext.Mentions(prompt).ToList())
            {
                if (mention.Kind != TicketContext.MentionKind.Full
                    && !whole.Contains(mention.Key)
                    && known.States.GetValueOrDefault(mention.Version) is { } state)
                {
                    prompt = prompt.Replace(mention.Text, TicketContext.FullFromState(mention.Key, mention.Version, state), StringComparison.Ordinal);
                }

                whole.Add(mention.Key);
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

    /// <summary>
    /// Old turns, before the question being answered, lose what they no longer need: long tool results and long tool call
    /// arguments become a short note, and the model calls the tool again when it needs them. The turn in progress stays
    /// whole. Only the request changes; the stored turns stay as they are.
    /// </summary>
    public static List<ConversationMessage> Compact(List<ConversationMessage> history)
    {
        var current = history.FindLastIndex(item => item.Role == ConversationRole.User);
        if (current <= 0)
        {
            return history;
        }

        var calls = new Dictionary<string, AssistantToolCall>();
        var result = new List<ConversationMessage>(history.Count);
        for (var index = 0; index < history.Count; index++)
        {
            var message = history[index];
            if (message.Role == ConversationRole.Assistant)
            {
                foreach (var call in AssistantViews.ToolCalls(message))
                {
                    calls[call.Id] = call;
                }
            }

            result.Add(index < current ? CompactTurn(message, calls) : message);
        }

        return result;
    }

    /// <summary>Tool results and arguments longer than this many characters are left out of old turns.</summary>
    public const int CompactAbove = 2048;

    /// <summary>String values longer than this, inside long arguments, are left out; the rest stays, so the call still reads.</summary>
    private const int ArgumentValueLength = 200;

    private static ConversationMessage CompactTurn(ConversationMessage message, Dictionary<string, AssistantToolCall> calls)
    {
        if (message.Role == ConversationRole.ToolResults && message.ToolsJson is { } resultsJson)
        {
            var results = JsonSerializer.Deserialize<List<AssistantToolOutcome>>(resultsJson, AssistantViews.Json) ?? [];
            if (!results.Any(result => result.Content.Length > CompactAbove))
            {
                return message;
            }

            var compacted = results.Select(result => result.Content.Length <= CompactAbove
                ? result
                : result with { Content = LeftOutResult(calls.GetValueOrDefault(result.ToolCallId), result.Content.Length) }).ToList();
            return message.WithTools(JsonSerializer.Serialize(compacted, AssistantViews.Json), message.NativeJson);
        }

        if (message.Role == ConversationRole.Assistant && message.ToolsJson is { } callsJson)
        {
            var list = AssistantViews.ToolCalls(message);
            if (!list.Any(call => call.Arguments.Length > CompactAbove))
            {
                return message;
            }

            var compacted = list.Select(call => call.Arguments.Length <= CompactAbove ? call : call with { Arguments = ShrinkArguments(call.Arguments) }).ToList();
            return message.WithTools(JsonSerializer.Serialize(compacted, AssistantViews.Json), ShrinkNative(message.NativeJson));
        }

        return message;
    }

    private static string LeftOutResult(AssistantToolCall? call, int length)
    {
        var what = call is null ? "tool" : $"{call.Name}{Subject(call.Arguments)}";
        return $"({what} result, {length:N0} characters, left out of this old turn; call the tool again if you need it)";
    }

    // The ticket or space a call was about, so the note says which result it was.
    private static string Subject(string arguments)
    {
        try
        {
            var node = JsonNode.Parse(arguments) as JsonObject;
            foreach (var name in new[] { "key", "space", "id" })
            {
                if (node?[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length is > 0 and <= 64)
                {
                    return $" {text}";
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    /// <summary>The same arguments, still valid JSON, with every long string replaced by a note of its length.</summary>
    public static string ShrinkArguments(string arguments)
    {
        try
        {
            return Shrink(JsonNode.Parse(arguments))?.ToJsonString() ?? "{}";
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    private static JsonNode? Shrink(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(pair => KeyValuePair.Create(pair.Key, Shrink(pair.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Shrink)]),
        JsonValue value when value.TryGetValue<string>(out var text) && text.Length > ArgumentValueLength
            => JsonValue.Create($"({text.Length:N0} characters, left out of this old turn)"),
        _ => node?.DeepClone(),
    };

    /// <summary>Claude's own blocks of the turn, with the input of long tool_use blocks shrunk the same way.</summary>
    private static string? ShrinkNative(string? native)
    {
        if (native is null)
        {
            return null;
        }

        var blocks = JsonNode.Parse(native) as JsonArray;
        if (blocks is null)
        {
            return native;
        }

        foreach (var block in blocks.OfType<JsonObject>())
        {
            if (block["type"]?.GetValue<string>() == "tool_use" && block["input"] is { } input && input.ToJsonString().Length > CompactAbove)
            {
                block["input"] = Shrink(input);
            }
        }

        return blocks.ToJsonString();
    }

    /// <summary>Roughly what the turns weigh in the request body, in bytes.</summary>
    internal static int Weight(IEnumerable<ConversationMessage> messages) => messages.Sum(message =>
        Encoding.UTF8.GetByteCount(message.Prompt ?? message.Text)
        + Encoding.UTF8.GetByteCount(message.ToolsJson ?? string.Empty)
        + Encoding.UTF8.GetByteCount(message.NativeJson ?? string.Empty)
        + 64);
}
