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
    private async Task<AiConnectionDto> UseAsync(AiProvider provider, int? maxRequestKb = null)
    {
        var admin = await AdminAsync();
        var response = await admin.Http.PostAsJsonAsync("/api/v1/assistant-admin/connection", new
        {
            name = $"{provider} {Guid.NewGuid():N}"[..30],
            provider,
            baseUrl = provider == AiProvider.Anthropic ? FakeAiServer.AnthropicUrl : FakeAiServer.OpenAiUrl,
            apiKey = "secret-key",
            model = provider == AiProvider.Anthropic ? "claude-opus-5-5" : "fake-model",
            maxRequestKb,
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
    public async Task The_first_answer_gets_the_conversation_a_summarized_title_unless_the_person_renamed_it()
    {
        await UseAsync(AiProvider.OpenAiCompatible);
        var client = await fixture.CreateUserClientAsync(Token);
        var named = await ConversationAsync(client);
        var renamed = await ConversationAsync(client);
        Ai.Script(new FakeAiReply("Plan it in the backlog."), new FakeAiReply("Plan it in the backlog."));

        await AskAsync(client, named.Id, $"{FakeAiServer.NameMe} How do I plan a sprint?");
        (await client.Http.PostAsJsonAsync($"/api/v1/assistant/{renamed.Id}/rename", new { title = "Mine" }, Token)).EnsureSuccessStatusCode();
        await AskAsync(client, renamed.Id, $"{FakeAiServer.NameMe} How do I plan a sprint?");

        async Task<string> TitleAsync(Guid id) =>
            (await client.Http.GetFromJsonAsync<ConversationDetailDto>($"/api/v1/assistant/{id}", Token))!.Conversation.Title;

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await TitleAsync(named.Id) != "Sprint planning help" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, Token);
        }

        (await TitleAsync(named.Id)).ShouldBe("Sprint planning help");
        (await TitleAsync(renamed.Id)).ShouldBe("Mine");
        var asked = Ai.TitleRequests.Last(body => body.ToJsonString().Contains("How do I plan a sprint", StringComparison.Ordinal));
        asked["tools"].ShouldBeNull();
        asked.ToJsonString().ShouldContain("Plan it in the backlog.");
        asked.ToJsonString().ShouldNotContain("<context>");
    }

    [Fact]
    public async Task The_issue_the_person_has_open_goes_with_the_question_when_they_can_read_it()
    {
        await UseAsync(AiProvider.OpenAiCompatible);
        var owner = await fixture.CreateUserClientAsync(Token);
        var (_, issue) = await IssueAsync(owner, "Checkout button misaligned");
        var conversation = await ConversationAsync(owner);
        Ai.Script(new FakeAiReply("It is about the checkout button."));

        await StreamAsync(owner, $"/api/v1/assistant/conversations/{conversation.Id}/messages", new { text = "Summarize this page", issueId = issue.Id });

        var question = Ai.Bodies.Single()["messages"]!.AsArray().Last()!["content"]!.ToJsonString();
        question.ShouldContain($"[{issue.Key}](/issues/{issue.Key})");
        question.ShouldContain("Checkout button misaligned");
        question.ShouldContain("this page");

        var outsider = await fixture.CreateUserClientAsync(Token);
        var theirs = await ConversationAsync(outsider);
        Ai.Script(new FakeAiReply("Which page?"));
        await StreamAsync(outsider, $"/api/v1/assistant/conversations/{theirs.Id}/messages", new { text = "Summarize this page", issueId = issue.Id });
        var hidden = Ai.Bodies.Single()["messages"]!.AsArray().Last()!["content"]!.ToJsonString();
        hidden.ShouldNotContain(issue.Key);
        hidden.ShouldNotContain("Checkout button misaligned");
    }

    /// <summary>The text of a chat completions message, whether sent as a string or as parts.</summary>
    private static string Content(JsonNode? message) => message!["content"] switch
    {
        JsonValue value => value.GetValue<string>(),
        JsonArray parts => string.Concat(parts.Select(part => part!["text"]?.GetValue<string>())),
        _ => string.Empty,
    };

    private static List<string> UserTexts(JsonObject body) =>
        [.. body["messages"]!.AsArray().Where(message => message!["role"]!.GetValue<string>() == "user").Select(Content)];

    [Fact]
    public async Task An_issue_goes_to_the_model_once_until_it_changes()
    {
        await UseAsync(AiProvider.OpenAiCompatible);
        var owner = await fixture.CreateUserClientAsync(Token);
        var (_, issue) = await IssueAsync(owner, "Checkout totals are off by a cent");
        var conversation = await ConversationAsync(owner, issue.Id);

        Ai.Script(new FakeAiReply("A rounding issue."));
        await AskAsync(owner, conversation.Id, "What is this about?");
        Ai.Script(new FakeAiReply("Probably in the tax step."));
        await AskAsync(owner, conversation.Id, "Where should I look?");

        var second = UserTexts(Ai.Bodies.Single());
        second[0].ShouldContain("Checkout totals are off by a cent");
        second[1].ShouldNotContain("Checkout totals are off by a cent");
        second[1].ShouldContain("unchanged=\"true\"");

        var current = (await owner.Http.GetFromJsonAsync<IssueDto>($"/api/v1/issue/by-key?key={issue.Key}", Token))!;
        (await owner.Http.PutAsJsonAsync($"/api/v1/issue/{issue.Id}", new { concurrencyStamp = current.ConcurrencyStamp, fields = new { title = "Checkout totals are off by two cents" } }, Token))
            .EnsureSuccessStatusCode();
        Ai.Script(new FakeAiReply("Then it grew."));
        await AskAsync(owner, conversation.Id, "And now?");
        UserTexts(Ai.Bodies.Single())[2].ShouldContain("Checkout totals are off by two cents");
    }

    [Fact]
    public void Long_tool_results_are_cut_with_a_hint_to_narrow_the_query()
    {
        var clipped = AssistantChatAppService.Clip(new string('x', AssistantConsts.MaxToolResultLength + 500));
        clipped.Length.ShouldBeLessThan(AssistantConsts.MaxToolResultLength + 200);
        clipped.ShouldContain("truncated: 500 more characters");
        AssistantChatAppService.Clip("short").ShouldBe("short");
    }

    [Fact]
    public async Task Over_the_connection_limit_older_exchanges_are_left_out_whole()
    {
        await UseAsync(AiProvider.OpenAiCompatible, maxRequestKb: 120);
        var client = await fixture.CreateUserClientAsync(Token);
        var conversation = await ConversationAsync(client);
        for (var index = 0; index < 6; index++)
        {
            Ai.Script(new FakeAiReply($"Answer {index}."));
            await AskAsync(client, conversation.Id, $"Question {index} {new string('x', 19_000)}");
        }

        var sent = Ai.Bodies.Single();
        Encoding.UTF8.GetByteCount(sent.ToJsonString()).ShouldBeLessThan(140 * 1024);
        var questions = UserTexts(sent);
        questions[0].ShouldContain("Question 0");
        questions[^1].ShouldContain("Question 5");
        questions.ShouldContain(text => text.Contains(ConversationHistory.LeftOut, StringComparison.Ordinal));
        questions.ShouldNotContain(text => text.Contains("Question 1 ", StringComparison.Ordinal));

        // Every question that went still has its answer right after it.
        var roles = sent["messages"]!.AsArray().Select(message => message!["role"]!.GetValue<string>()).Where(role => role != "system").ToList();
        for (var index = 0; index < roles.Count - 1; index += 2)
        {
            roles[index].ShouldBe("user");
            roles[index + 1].ShouldBe("assistant");
        }
    }

    [Fact]
    public async Task A_request_the_provider_finds_too_large_reads_as_such()
    {
        await UseAsync(AiProvider.OpenAiCompatible);
        var client = await fixture.CreateUserClientAsync(Token);
        var conversation = await ConversationAsync(client);
        Ai.Script(new FakeAiReply("") { Status = HttpStatusCode.RequestEntityTooLarge });

        var events = await AskAsync(client, conversation.Id, "Hello");

        events.Single(item => item.Type == "error").Code.ShouldBe(AssistantErrors.RequestTooLarge);
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
    public async Task Admins_load_models_and_test_settings_before_saving()
    {
        var admin = await AdminAsync();
        Ai.Script();

        var openAi = await admin.Http.PostAsJsonAsync("/api/v1/assistant-admin/list-models",
            new { provider = AiProvider.OpenAiCompatible, baseUrl = FakeAiServer.OpenAiUrl, apiKey = "typed-key" }, Token);
        openAi.StatusCode.ShouldBe(HttpStatusCode.OK, await openAi.Content.ReadAsStringAsync(Token));
        (await openAi.Content.ReadFromJsonAsync<List<AiModelDto>>(Token))!.Select(model => model.Id).ShouldBe(["gpt-fake", "llama3.3"]);
        Ai.Requests.Single().Headers.Authorization!.Parameter.ShouldBe("typed-key");

        // Editing: a blank key field uses the stored key.
        var saved = await UseAsync(AiProvider.Anthropic);
        Ai.Script();
        var claude = await admin.Http.PostAsJsonAsync("/api/v1/assistant-admin/list-models",
            new { provider = AiProvider.Anthropic, baseUrl = FakeAiServer.AnthropicUrl, connectionId = saved.Id }, Token);
        claude.StatusCode.ShouldBe(HttpStatusCode.OK, await claude.Content.ReadAsStringAsync(Token));
        var models = (await claude.Content.ReadFromJsonAsync<List<AiModelDto>>(Token))!;
        models.Single(model => model.Id == "claude-opus-5-5").Name.ShouldBe("Claude Opus 5.5");
        Ai.Requests.Single().Headers.Authorization!.Parameter.ShouldBe("secret-key");

        Ai.Script(new FakeAiReply("Hello!"));
        var passed = await admin.Http.PostAsJsonAsync("/api/v1/assistant-admin/test-settings",
            new { provider = AiProvider.OpenAiCompatible, baseUrl = FakeAiServer.OpenAiUrl, model = "llama3.3" }, Token);
        var result = (await passed.Content.ReadFromJsonAsync<TestAiConnectionResultDto>(Token))!;
        result.Succeeded.ShouldBeTrue();
        result.Message.ShouldBe("Hello!");
        Ai.Bodies.Single()["model"]!.GetValue<string>().ShouldBe("llama3.3");

        Ai.Script();
        var failed = await admin.Http.PostAsJsonAsync("/api/v1/assistant-admin/test-settings",
            new { provider = AiProvider.OpenAiCompatible, baseUrl = FakeAiServer.OpenAiUrl, model = "missing" }, Token);
        (await failed.Content.ReadFromJsonAsync<TestAiConnectionResultDto>(Token))!.Succeeded.ShouldBeFalse();

        var member = await fixture.CreateUserClientAsync(Token);
        (await member.Http.PostAsJsonAsync("/api/v1/assistant-admin/list-models", new { provider = AiProvider.OpenAiCompatible, baseUrl = FakeAiServer.OpenAiUrl }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
