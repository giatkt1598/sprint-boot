using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SprintBoot.Assistant;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>The assistant against scripted providers: what it streams, what it stores, and what it sends back to the model.</summary>
[Collection(ApiCollection.Name)]
public class AssistantTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeAiServer Ai => FakeAiServer.Instance;

    private async Task<AuthClient> AdminAsync()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        return admin;
    }

    /// <summary>Makes the given connection the only default and switches the assistant on.</summary>
    private async Task<AiConnectionDto> UseAsync(AiProvider provider)
    {
        var admin = await AdminAsync();
        var response = await admin.Http.PostAsJsonAsync("/api/v1/assistant-admin/connection", new
        {
            name = $"{provider} {Guid.NewGuid():N}"[..30],
            provider,
            baseUrl = provider == AiProvider.Anthropic ? FakeAiServer.AnthropicUrl : FakeAiServer.OpenAiUrl,
            apiKey = "secret-key",
            model = provider == AiProvider.Anthropic ? "claude-opus-5-5" : "fake-model",
            isDefault = true,
        }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        (await admin.Http.PutAsJsonAsync("/api/v1/assistant-admin", new { enabled = true }, Token)).EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AiConnectionDto>(Token))!;
    }

    private static async Task<(ProjectDto Project, IssueDto Issue)> IssueAsync(AuthClient client, string title = "Payment fails on retry")
    {
        var key = $"A{Random.Shared.Next(100000, 999999)}";
        var project = (await (await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Assist", key, type = 0 }, Token)).Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{project.Id}", Token);
        var created = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId = types!.Single(type => type.Name == "Task").Id, title }, Token);
        return (project, (await created.Content.ReadFromJsonAsync<IssueDto>(Token))!);
    }

    private static async Task<ConversationDto> ConversationAsync(AuthClient client, Guid? issueId = null)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/assistant", new { issueId }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ConversationDto>(Token))!;
    }

    private static async Task<List<AssistantEventDto>> StreamAsync(AuthClient client, string path, object? body = null)
    {
        var response = await client.Http.PostAsync(path, body is null ? null : JsonContent.Create(body), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        var text = await response.Content.ReadAsStringAsync(Token);
        return [.. text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(chunk => JsonSerializer.Deserialize<AssistantEventDto>(chunk["data: ".Length..], Json)!)];
    }

    private static Task<List<AssistantEventDto>> AskAsync(AuthClient client, Guid conversationId, string text) =>
        StreamAsync(client, $"/api/v1/assistant/conversations/{conversationId}/messages", new { text });

    private static string Deltas(List<AssistantEventDto> events) =>
        string.Concat(events.Where(item => item.Type == "delta").Select(item => item.Text));

    [Fact]
    public void Every_mcp_tool_is_offered_with_its_schema_and_writes_need_confirmation()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<IAssistantToolbox>().Tools;

        tools.Count.ShouldBe(25);
        tools.Select(tool => tool.Name).ShouldBeUnique();
        tools.Single(tool => tool.Name == "search_issues").ReadOnly.ShouldBeTrue();
        tools.Single(tool => tool.Name == "get_issue").ReadOnly.ShouldBeTrue();
        foreach (var write in new[] { "create_issue", "update_issue", "add_comment", "transition_issue", "log_work", "move_to_sprint" })
        {
            tools.Single(tool => tool.Name == write).ReadOnly.ShouldBeFalse(write);
        }

        var search = tools.Single(tool => tool.Name == "search_issues");
        search.Description.ShouldNotBeNullOrWhiteSpace();
        search.InputSchema.GetProperty("properties").TryGetProperty("text", out _).ShouldBeTrue();
        search.InputSchema.GetProperty("properties").TryGetProperty("cancellationToken", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_plain_answer_streams_is_stored_and_stays_private()
    {
        var connection = await UseAsync(AiProvider.OpenAiCompatible);
        connection.HasApiKey.ShouldBeTrue();
        (await (await AdminAsync()).Http.GetStringAsync("/api/v1/assistant-admin", Token)).ShouldNotContain("secret-key");

        var client = await fixture.CreateUserClientAsync(Token);
        (await client.Http.GetFromJsonAsync<AssistantStatusDto>("/api/v1/assistant/status", Token))!.Available.ShouldBeTrue();
        var conversation = await ConversationAsync(client);
        Ai.Script(new FakeAiReply("Sprint Boot tracks **issues**."));

        var events = await AskAsync(client, conversation.Id, "What is Sprint Boot?");

        Deltas(events).ShouldBe("Sprint Boot tracks **issues**.");
        var message = events.Single(item => item.Type == "message").Message!;
        message.Html.ShouldNotBeNull().ShouldContain("<strong>issues</strong>");
        events.Last().Type.ShouldBe("done");

        var sent = Ai.Bodies.Single();
        sent["stream"]!.GetValue<bool>().ShouldBeTrue();
        sent["messages"]![0]!["role"]!.GetValue<string>().ShouldBe("system");
        sent["messages"]!.AsArray().Last()!["content"]!.ToJsonString().ShouldContain("What is Sprint Boot?");
        sent["tools"]!.AsArray().Select(tool => tool!["function"]!["name"]!.GetValue<string>()).ShouldContain("search_issues");
        Ai.Requests.Single().Headers.Authorization!.Parameter.ShouldBe("secret-key");

        var detail = (await client.Http.GetFromJsonAsync<ConversationDetailDto>($"/api/v1/assistant/{conversation.Id}", Token))!;
        detail.Conversation.Title.ShouldBe("What is Sprint Boot?");
        detail.Messages.Select(item => item.Role).ShouldBe(["user", "assistant"]);

        var other = await fixture.CreateUserClientAsync(Token);
        (await other.Http.GetAsync($"/api/v1/assistant/{conversation.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Http.PostAsJsonAsync($"/api/v1/assistant/conversations/{conversation.Id}/messages", new { text = "hi" }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Http.GetFromJsonAsync<List<ConversationDto>>("/api/v1/assistant", Token))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Read_tools_run_at_once_and_their_results_go_back_to_the_model()
    {
        await UseAsync(AiProvider.OpenAiCompatible);
        var client = await fixture.CreateUserClientAsync(Token);
        var (_, issue) = await IssueAsync(client, "Checkout payment fails");
        var conversation = await ConversationAsync(client);
        Ai.Script(
            new FakeAiReply("", ("search_issues", new { text = "payment" })),
            new FakeAiReply($"Found [{issue.Key}](/issues/{issue.Key})."));

        var events = await AskAsync(client, conversation.Id, "Which issues mention payment?");

        events.Where(item => item.Type == "tool").Select(item => item.State).ShouldBe(["running", "done"]);
        var final = events.Last(item => item.Type == "message").Message!;
        final.Sources.ShouldBe([issue.Key]);

        var second = Ai.Bodies[1]["messages"]!.AsArray();
        var toolMessage = second.Single(message => message!["role"]!.GetValue<string>() == "tool")!;
        toolMessage["tool_call_id"]!.GetValue<string>().ShouldBe("call_0");
        toolMessage["content"]!.ToJsonString().ShouldContain(issue.Key);
    }

    [Fact]
    public async Task Writes_wait_for_confirmation_and_declined_ones_never_run()
    {
        await UseAsync(AiProvider.OpenAiCompatible);
        var client = await fixture.CreateUserClientAsync(Token);
        var (_, issue) = await IssueAsync(client);
        var conversation = await ConversationAsync(client, issue.Id);

        Ai.Script(new FakeAiReply("I will add the comment.", ("add_comment", new { key = issue.Key, body = "Looked into it" })));
        var proposed = await AskAsync(client, conversation.Id, "Comment that I looked into it");
        var action = proposed.Single(item => item.Type == "action").Action!;
        action.State.ShouldBe(AssistantActionState.Proposed);
        action.Tool.ShouldBe("add_comment");
        // The issue went along as context.
        Ai.Bodies.Single()["messages"]!.AsArray().Last()!["content"]!.ToJsonString().ShouldContain(issue.Title);
        (await client.Http.GetFromJsonAsync<List<CommentDto>>($"/api/v1/comment?issueId={issue.Id}", Token))!.ShouldBeEmpty();

        Ai.Script(new FakeAiReply("Done, the comment is on the issue."));
        var confirmed = await StreamAsync(client, $"/api/v1/assistant/conversations/{conversation.Id}/actions/{action.Id}/confirm");
        confirmed.First(item => item.Type == "action").Action!.State.ShouldBe(AssistantActionState.Confirmed);
        Deltas(confirmed).ShouldBe("Done, the comment is on the issue.");
        (await client.Http.GetFromJsonAsync<List<CommentDto>>($"/api/v1/comment?issueId={issue.Id}", Token))!.ShouldHaveSingleItem().Body.ShouldContain("Looked into it");

        // Confirming twice changes nothing.
        (await StreamAsync(client, $"/api/v1/assistant/conversations/{conversation.Id}/actions/{action.Id}/confirm"))
            .Single().Code.ShouldBe(AssistantErrors.ActionNotPending);

        Ai.Script(
            new FakeAiReply("", ("add_comment", new { key = issue.Key, body = "Second" })),
            new FakeAiReply("Understood, I left it."));
        var again = await AskAsync(client, conversation.Id, "Add another comment");
        var second = again.Single(item => item.Type == "action").Action!;
        var declined = await StreamAsync(client, $"/api/v1/assistant/conversations/{conversation.Id}/actions/{second.Id}/decline");
        declined.First(item => item.Type == "action").Action!.State.ShouldBe(AssistantActionState.Declined);
        (await client.Http.GetFromJsonAsync<List<CommentDto>>($"/api/v1/comment?issueId={issue.Id}", Token))!.Count.ShouldBe(1);
        Ai.Bodies.Last()["messages"]!.AsArray().Last()!["content"]!.ToJsonString().ShouldContain("declined");
    }

    [Fact]
    public async Task Tools_see_only_what_the_person_may_see()
    {
        await UseAsync(AiProvider.OpenAiCompatible);
        var owner = await fixture.CreateUserClientAsync(Token);
        var (_, hidden) = await IssueAsync(owner, "Secret roadmap");
        var outsider = await fixture.CreateUserClientAsync(Token);
        (await outsider.Http.PostAsJsonAsync("/api/v1/assistant", new { issueId = hidden.Id }, Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var conversation = await ConversationAsync(outsider);
        Ai.Script(new FakeAiReply("", ("get_issue", new { key = hidden.Key })), new FakeAiReply("I cannot find that issue."));
        var events = await AskAsync(outsider, conversation.Id, $"What is {hidden.Key} about?");

        events.Single(item => item.Type == "tool" && item.State != "running").State.ShouldBe("failed");
        var result = Ai.Bodies[1]["messages"]!.AsArray().Single(message => message!["role"]!.GetValue<string>() == "tool")!;
        result["content"]!.ToJsonString().ShouldNotContain("Secret roadmap");
    }

    [Fact]
    public async Task Claude_gets_its_signed_thinking_back_unchanged_with_caching_and_fallbacks()
    {
        await UseAsync(AiProvider.Anthropic);
        var client = await fixture.CreateUserClientAsync(Token);
        var (_, issue) = await IssueAsync(client, "Login button misaligned");
        var conversation = await ConversationAsync(client);
        Ai.Script(
            new FakeAiReply("Let me look.", ("search_issues", new { text = "login" })) { Thinking = "find login issues" },
            new FakeAiReply($"See [{issue.Key}](/issues/{issue.Key})."));

        var events = await AskAsync(client, conversation.Id, "Any login issues?");

        Deltas(events).ShouldBe($"Let me look.See [{issue.Key}](/issues/{issue.Key}).");
        var first = Ai.Requests.First();
        first.Path.ShouldEndWith("/v1/messages");
        first.Headers.Authorization!.Parameter.ShouldBe("secret-key");
        string.Join(",", first.Headers.GetValues("anthropic-beta")).ShouldContain("server-side-fallback-2026-07-01");
        first.Body["fallbacks"]!.GetValue<string>().ShouldBe("default");
        first.Body["cache_control"]!["type"]!.GetValue<string>().ShouldBe("ephemeral");
        first.Body["model"]!.GetValue<string>().ShouldBe("claude-opus-5-5");

        var replay = Ai.Bodies[1]["messages"]!.AsArray();
        var assistant = replay.Single(message => message!["role"]!.GetValue<string>() == "assistant")!["content"]!.AsArray();
        assistant[0]!["type"]!.GetValue<string>().ShouldBe("thinking");
        assistant[0]!["thinking"]!.GetValue<string>().ShouldBe("find login issues");
        assistant[0]!["signature"]!.GetValue<string>().ShouldBe("sig-find login issues");
        assistant.Last()!["type"]!.GetValue<string>().ShouldBe("tool_use");
        var results = replay.Last()!["content"]!.AsArray();
        results[0]!["type"]!.GetValue<string>().ShouldBe("tool_result");
        results[0]!["content"]!.ToJsonString().ShouldContain(issue.Key);
    }

    [Fact]
    public async Task Without_the_switch_the_assistant_is_unavailable_and_only_admins_configure_it()
    {
        await UseAsync(AiProvider.OpenAiCompatible);
        var admin = await AdminAsync();
        (await admin.Http.PutAsJsonAsync("/api/v1/assistant-admin", new { enabled = false }, Token)).EnsureSuccessStatusCode();
        try
        {
            var client = await fixture.CreateUserClientAsync(Token);
            (await client.Http.GetFromJsonAsync<AssistantStatusDto>("/api/v1/assistant/status", Token))!.Available.ShouldBeFalse();
            var conversation = await ConversationAsync(client);
            (await AskAsync(client, conversation.Id, "Hello")).Single().Code.ShouldBe(AssistantErrors.Unavailable);

            (await client.Http.GetAsync("/api/v1/assistant-admin", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            var invalid = await admin.Http.PostAsJsonAsync("/api/v1/assistant-admin/connection", new { name = "No url", provider = AiProvider.OpenAiCompatible, model = "m" }, Token);
            (await invalid.Content.ReadAsStringAsync(Token)).ShouldContain(AssistantErrors.BaseUrlRequired);
        }
        finally
        {
            (await admin.Http.PutAsJsonAsync("/api/v1/assistant-admin", new { enabled = true }, Token)).EnsureSuccessStatusCode();
        }
    }
}
