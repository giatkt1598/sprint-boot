using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using SprintBoot.Assistant;
using Xunit;

namespace SprintBoot.UnitTests;

/// <summary>What a ticket conversation sends: tickets only once then their changes, and old turns without bulky tool data.</summary>
public class ConversationCompactionTests
{
    private const string Key = "SB-62";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string Ticket(string title, string status = "To Do", params (string Id, string Body, string? Reply)[] comments)
    {
        var thread = new JsonArray();
        foreach (var (id, body, reply) in comments)
        {
            var comment = new JsonObject { ["id"] = id, ["author"] = "Ann <ann@test.local>", ["body"] = body };
            if (reply is null)
            {
                thread.Add(comment);
            }
            else
            {
                var parent = thread.OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == reply);
                ((parent["replies"] ??= new JsonArray()) as JsonArray)!.Add(comment);
            }
        }

        return new JsonObject
        {
            ["key"] = Key,
            ["title"] = title,
            ["status"] = status,
            ["assignee"] = "Bob <bob@test.local>",
            ["description"] = new string('d', 4000),
            ["comments"] = thread,
        }.ToJsonString();
    }

    [Fact]
    public void The_first_question_carries_the_whole_ticket_and_then_only_a_header()
    {
        var content = Ticket("Payment fails");
        var first = TicketContext.ForQuestion(Key, content, []);
        first.ShouldContain(new string('d', 4000));

        var again = TicketContext.ForQuestion(Key, content, [first]);
        again.ShouldContain("unchanged=\"true\"");
        again.ShouldContain("SB-62 · Payment fails · status To Do · assignee Bob <bob@test.local>");
        again.Length.ShouldBeLessThan(300);
    }

    [Fact]
    public void A_changed_ticket_sends_only_the_changed_fields_and_the_new_comments()
    {
        var first = TicketContext.ForQuestion(Key, Ticket("Payment fails", "To Do", ("c1", "First", null)), []);
        var changed = Ticket("Payment fails twice", "In Progress", ("c1", "First", null), ("c2", "Reply", "c1"));

        var block = TicketContext.ForQuestion(Key, changed, [first]);

        block.ShouldContain("changes-from=");
        block.ShouldContain("Payment fails twice");
        block.ShouldContain("In Progress");
        block.ShouldContain("\"newComments\"");
        block.ShouldContain("\"replyTo\":\"c1\"");
        block.ShouldNotContain(new string('d', 100));
        block.ShouldNotContain("\"First\"");
    }

    [Fact]
    public void The_ticket_the_model_knows_is_rebuilt_from_the_copy_and_every_change_after_it()
    {
        var v1 = Ticket("One", "To Do", ("c1", "First", null), ("c2", "Second", null));
        var v2 = Ticket("Two", "To Do", ("c1", "First edited", null), ("c2", "Second", null));
        var v3 = Ticket("Two", "Done", ("c1", "First edited", null), ("c3", "Third", null));
        var prompts = new List<string>();
        prompts.Add(TicketContext.ForQuestion(Key, v1, prompts));
        prompts.Add(TicketContext.ForQuestion(Key, v2, prompts));
        prompts.Add(TicketContext.ForQuestion(Key, v3, prompts));

        // Each change is against the one before, not against the first copy.
        prompts[2].ShouldNotContain("\"title\"");
        prompts[2].ShouldContain("\"removedComments\":[\"c2\"]");

        var (states, latest) = TicketContext.Known(prompts);
        var known = states[latest[Key]]!;
        JsonNode.DeepEquals(known, TicketContext.Normalize(v3)).ShouldBeTrue();
        TicketContext.ForQuestion(Key, v3, prompts).ShouldContain("unchanged=\"true\"");
    }

    [Fact]
    public void Content_that_is_no_json_goes_whole_when_it_changes()
    {
        var first = TicketContext.ForQuestion(Key, "plain text ticket", []);
        TicketContext.ForQuestion(Key, "plain text ticket, edited", [first]).ShouldContain("plain text ticket, edited");
    }

    [Fact]
    public void Old_turns_lose_long_tool_data_and_the_turn_in_progress_keeps_it()
    {
        var conversation = Guid.NewGuid();
        var longArguments = JsonSerializer.Serialize(new { key = Key, description = new string('x', 5000) });
        var longResult = new string('r', 12000);
        var native = new JsonArray(
            new JsonObject { ["type"] = "text", ["text"] = "Updating." },
            new JsonObject { ["type"] = "tool_use", ["id"] = "t1", ["name"] = "update_ticket", ["input"] = JsonNode.Parse(longArguments) }).ToJsonString();
        List<ConversationMessage> history =
        [
            ConversationMessage.FromUser(Guid.NewGuid(), conversation, "Rewrite it", "Rewrite it", DateTime.UtcNow),
            Assistant(conversation, [new AssistantToolCall("t1", "update_ticket", longArguments)], native),
            Results(conversation, [new AssistantToolOutcome("t1", longResult, false)]),
            ConversationMessage.FromUser(Guid.NewGuid(), conversation, "Now read it", "Now read it", DateTime.UtcNow),
            Assistant(conversation, [new AssistantToolCall("t2", "get_ticket", JsonSerializer.Serialize(new { key = Key }))], null),
            Results(conversation, [new AssistantToolOutcome("t2", longResult, false)]),
        ];

        var compacted = ConversationHistory.Compact(history);

        var oldCall = AssistantViews.ToolCalls(compacted[1]).Single();
        var arguments = JsonNode.Parse(oldCall.Arguments)!;
        arguments["key"]!.GetValue<string>().ShouldBe(Key);
        arguments["description"]!.GetValue<string>().ShouldContain("5,000 characters, left out");
        var nativeInput = JsonNode.Parse(compacted[1].NativeJson!)!.AsArray()[1]!["input"]!;
        nativeInput["description"]!.GetValue<string>().ShouldContain("left out");
        JsonNode.Parse(compacted[1].NativeJson!)!.AsArray()[0]!["text"]!.GetValue<string>().ShouldBe("Updating.");

        var oldResult = Outcomes(compacted[2]).Single();
        oldResult.ToolCallId.ShouldBe("t1");
        oldResult.Content.ShouldBe("(update_ticket SB-62 result, 12,000 characters, left out of this old turn; call the tool again if you need it)");

        // The turn being answered stays whole, and the stored turns are untouched.
        Outcomes(compacted[5]).Single().Content.ShouldBe(longResult);
        compacted[4].ShouldBeSameAs(history[4]);
        AssistantViews.ToolCalls(history[1]).Single().Arguments.ShouldBe(longArguments);
        Outcomes(history[2]).Single().Content.ShouldBe(longResult);
    }

    [Fact]
    public void A_single_question_is_never_compacted()
    {
        var conversation = Guid.NewGuid();
        List<ConversationMessage> history =
        [
            ConversationMessage.FromUser(Guid.NewGuid(), conversation, "Read", "Read", DateTime.UtcNow),
            Assistant(conversation, [new AssistantToolCall("t1", "get_ticket", "{}")], null),
            Results(conversation, [new AssistantToolOutcome("t1", new string('r', 9000), false)]),
        ];

        ConversationHistory.Compact(history).ShouldBe(history);
    }

    private static ConversationMessage Assistant(Guid conversation, List<AssistantToolCall> calls, string? native) =>
        ConversationMessage.FromAssistant(
            Guid.NewGuid(), conversation, string.Empty, JsonSerializer.Serialize(calls, Web), native,
            native is null ? AiProvider.OpenAiCompatible : AiProvider.Anthropic, "model", null, new ChatUsage(0, 0, 0), DateTime.UtcNow);

    private static ConversationMessage Results(Guid conversation, List<AssistantToolOutcome> outcomes) =>
        ConversationMessage.FromToolResults(Guid.NewGuid(), conversation, JsonSerializer.Serialize(outcomes, Web), DateTime.UtcNow);

    private static List<AssistantToolOutcome> Outcomes(ConversationMessage message) =>
        JsonSerializer.Deserialize<List<AssistantToolOutcome>>(message.ToolsJson!, Web)!;
}
