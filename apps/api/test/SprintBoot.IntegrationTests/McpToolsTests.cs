using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class McpToolsTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class ToolError(string message) : Exception(message);

    private sealed record Agent(HttpClient Http, AuthClient Owner, ProjectDto Space);

    private async Task<Agent> AgentAsync(bool readOnly = false)
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var key = $"A{Random.Shared.Next(100000, 999999)}";
        var created = await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Agent space", key, type = 0 }, Token);
        created.EnsureSuccessStatusCode();
        var space = (await created.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        var token = await owner.Http.PostAsJsonAsync("/api/v1/personal-token", new { name = "Agent", kind = PersonalTokenKind.Mcp, readOnly, expiresInDays = 30 }, Token);
        token.EnsureSuccessStatusCode();
        var http = fixture.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await token.Content.ReadFromJsonAsync<CreatedPersonalTokenDto>(Token))!.Token);
        return new Agent(http, owner, space);
    }

    private static async Task<JsonElement> CallAsync(HttpClient http, string tool, object arguments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments } }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        var response = await http.SendAsync(request, Token);
        var body = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var json = body.Split('\n').FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal))?[5..] ?? body;
        var root = JsonDocument.Parse(json).RootElement;
        root.TryGetProperty("result", out var result).ShouldBeTrue(body);
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        if (result.TryGetProperty("isError", out var isError) && isError.GetBoolean())
        {
            throw new ToolError(text);
        }

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    [Fact]
    public async Task Every_parameter_is_in_the_tool_schemas()
    {
        var agent = await AgentAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        var body = await (await agent.Http.SendAsync(request, Token)).Content.ReadAsStringAsync(Token);
        var json = body.Split('\n').FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal))?[5..] ?? body;
        var tools = JsonDocument.Parse(json).RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .ToDictionary(tool => Text(tool, "name"), tool => tool.GetProperty("inputSchema").GetProperty("properties"));

        // Arrays and dictionaries can be taken for services and silently dropped from a schema.
        tools["move_to_sprint"].TryGetProperty("keys", out _).ShouldBeTrue();
        tools["create_issues"].TryGetProperty("items", out _).ShouldBeTrue();
        tools["edit_description"].TryGetProperty("edits", out _).ShouldBeTrue();
        tools["update_issue"].TryGetProperty("labels", out _).ShouldBeTrue();
        tools["transition_issue"].TryGetProperty("fields", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Issues_are_created_with_every_field_and_read_back_as_markdown()
    {
        var agent = await AgentAsync();
        var key = agent.Space.Key;
        var epic = await CallAsync(agent.Http, "create_issue", new { space = key, title = "Payments", type = "Epic" });

        var issue = await CallAsync(agent.Http, "create_issue", new
        {
            space = key,
            title = "Card form",
            type = "Story",
            description = "## Goal\n\nAccept **cards**.\n\n- [ ] validate\n- [x] design\n\n:::callout\nPCI scope\n:::",
            priority = "High",
            assignee = "me",
            parent = Text(epic, "key"),
            labels = new[] { "payments", "web" },
            storyPoints = 5,
            startDate = "2026-11-02",
            dueDate = "2026-11-20",
        });

        var read = await CallAsync(agent.Http, "get_issue", new { key = Text(issue, "key") });
        Text(read, "type").ShouldBe("Story");
        Text(read, "priority").ShouldBe("High");
        Text(read, "assignee").ShouldContain("@test.local");
        read.GetProperty("parent").GetProperty("key").GetString().ShouldBe(Text(epic, "key"));
        read.GetProperty("labels").EnumerateArray().Select(item => item.GetString()).ShouldBe(["payments", "web"], ignoreOrder: true);
        read.GetProperty("storyPoints").GetDecimal().ShouldBe(5);
        Text(read, "dueDate").ShouldBe("2026-11-20");
        Text(read, "description").ShouldBe("## Goal\n\nAccept **cards**.\n\n- [ ] validate\n- [x] design\n\n:::callout\nPCI scope\n:::");

        var html = await CallAsync(agent.Http, "get_issue", new { key = Text(issue, "key"), format = "html" });
        Text(html, "description").ShouldContain("data-type=\"taskList\"");

        var fields = await CallAsync(agent.Http, "get_space_fields", new { space = key });
        fields.GetProperty("labels").EnumerateArray().Select(item => item.GetString()).ShouldContain("payments");
        fields.GetProperty("epics")[0].GetProperty("key").GetString().ShouldBe(Text(epic, "key"));
    }

    [Fact]
    public async Task A_repeated_idempotency_key_returns_the_first_issue()
    {
        var agent = await AgentAsync();
        var first = await CallAsync(agent.Http, "create_issue", new { space = agent.Space.Key, title = "Once", idempotencyKey = "abc-1" });
        var again = await CallAsync(agent.Http, "create_issue", new { space = agent.Space.Key, title = "Once", idempotencyKey = "abc-1" });

        Text(again, "key").ShouldBe(Text(first, "key"));
        again.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        var found = await CallAsync(agent.Http, "search_issues", new { space = agent.Space.Key });
        found.GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Search_filters_and_pages()
    {
        var agent = await AgentAsync();
        var key = agent.Space.Key;
        var batch = await CallAsync(agent.Http, "create_issues", new
        {
            space = key,
            items = new object[]
            {
                new { title = "Bug one", type = "Bug", priority = "High", labels = new[] { "ops" } },
                new { title = "Bug two", type = "Bug" },
                new { title = "Story", type = "Story", assignee = "me" },
                new { title = "Child", type = "Subtask", parentRef = 2 },
                new { title = "Broken", type = "Nope" },
            },
        });
        batch.GetProperty("created").GetInt32().ShouldBe(4);
        batch.GetProperty("results")[4].GetProperty("error").GetString()!.ShouldContain("No issue type is named Nope");

        (await CallAsync(agent.Http, "search_issues", new { space = key, types = new[] { "bug" } })).GetProperty("total").GetInt32().ShouldBe(2);
        (await CallAsync(agent.Http, "search_issues", new { space = key, priorities = new[] { "High" }, labels = new[] { "ops" } }))
            .GetProperty("issues")[0].GetProperty("title").GetString().ShouldBe("Bug one");
        (await CallAsync(agent.Http, "search_issues", new { space = key, assignees = new[] { "me" } })).GetProperty("total").GetInt32().ShouldBe(1);
        (await CallAsync(agent.Http, "search_issues", new { parent = $"{key}-3" })).GetProperty("issues")[0].GetProperty("title").GetString().ShouldBe("Child");

        var page = await CallAsync(agent.Http, "search_issues", new { space = key, limit = 3 });
        page.GetProperty("nextOffset").GetInt32().ShouldBe(3);
        var rest = await CallAsync(agent.Http, "search_issues", new { space = key, limit = 3, offset = 3 });
        rest.GetProperty("issues").GetArrayLength().ShouldBe(1);
        rest.TryGetProperty("nextOffset", out _).ShouldBeFalse();

        await Should.ThrowAsync<ToolError>(() => CallAsync(agent.Http, "search_issues", new { space = key, statuses = new[] { "Nope" } }));
    }

    [Fact]
    public async Task Updates_change_clear_and_patch_fields()
    {
        var agent = await AgentAsync();
        var key = Text(await CallAsync(agent.Http, "create_issue", new
        {
            space = agent.Space.Key, title = "Old", description = "# Plan\n\nDraft\n\n## Risks\n\nMany", labels = new[] { "a", "b" }, assignee = "me", dueDate = "2026-12-01",
        }), "key");

        var updated = await CallAsync(agent.Http, "update_issue", new { key, title = "New", priority = "Low", addLabels = new[] { "c" }, removeLabels = new[] { "a" }, clear = new[] { "assignee", "dueDate" } });
        Text(updated, "title").ShouldBe("New");
        Text(updated, "priority").ShouldBe("Low");
        updated.GetProperty("labels").EnumerateArray().Select(item => item.GetString()).ShouldBe(["b", "c"], ignoreOrder: true);
        updated.TryGetProperty("assignee", out _).ShouldBeFalse();
        updated.TryGetProperty("dueDate", out _).ShouldBeFalse();

        var edited = await CallAsync(agent.Http, "edit_description", new
        {
            key,
            edits = new object[]
            {
                new { op = "replace", find = "Draft", text = "Final" },
                new { op = "replace_section", heading = "Risks", text = "None" },
                new { op = "append", text = $"Thanks @[Me](user:{agent.Owner.UserId})" },
            },
        });
        Text(edited, "description").ShouldBe($"# Plan\n\nFinal\n\n## Risks\n\nNone\n\nThanks @[Me](user:{agent.Owner.UserId})");

        var stale = Should.ThrowAsync<ToolError>(() => CallAsync(agent.Http, "update_issue", new { key, title = "X", concurrencyStamp = "stale" }));
        (await stale).Message.ShouldContain("changed since");
        await Should.ThrowAsync<ToolError>(() => CallAsync(agent.Http, "update_issue", new { key, clear = new[] { "title" } }));

        var second = Text(await CallAsync(agent.Http, "create_issue", new { space = agent.Space.Key, title = "Second" }), "key");
        var bulk = await CallAsync(agent.Http, "update_issues", new { keys = new[] { key, second, $"{agent.Space.Key}-99" }, assignee = "me", addLabels = new[] { "bulk" } });
        bulk.GetProperty("updated").GetInt32().ShouldBe(2);
        bulk.GetProperty("failed").GetInt32().ShouldBe(1);
        (await CallAsync(agent.Http, "search_issues", new { space = agent.Space.Key, labels = new[] { "bulk" } })).GetProperty("total").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Transitions_comments_work_logs_watchers_and_activity()
    {
        var agent = await AgentAsync();
        var key = Text(await CallAsync(agent.Http, "create_issue", new { space = agent.Space.Key, title = "Flow" }), "key");

        var options = await CallAsync(agent.Http, "list_transitions", new { key });
        options.GetProperty("transitions").EnumerateArray().Select(item => item.GetProperty("to").GetString()).ShouldContain("Done");
        var refused = await Should.ThrowAsync<ToolError>(() => CallAsync(agent.Http, "transition_issue", new { key, status = "Nowhere" }));
        refused.Message.ShouldContain("It can move to");
        Text(await CallAsync(agent.Http, "transition_issue", new { key, status = "done", comment = "Shipped **today**" }), "status").ShouldBe("Done");

        var comment = await CallAsync(agent.Http, "add_comment", new { key, body = "First *note*" });
        var id = Text(comment, "id");
        await CallAsync(agent.Http, "add_comment", new { key, body = "A reply", replyTo = id });
        Text(await CallAsync(agent.Http, "update_comment", new { id, body = "First **note**" }), "body").ShouldBe("First **note**");
        var thread = await CallAsync(agent.Http, "list_comments", new { key });
        var root = thread.GetProperty("comments").EnumerateArray().Single(item => Text(item, "id") == id);
        Text(root.GetProperty("replies")[0], "body").ShouldBe("A reply");

        var log = await CallAsync(agent.Http, "log_work", new { key, time = "1h 30m", date = "2026-11-02", note = "Pairing" });
        Text(log, "time").ShouldBe("1h 30m");
        await CallAsync(agent.Http, "update_work_log", new { key, id = Text(log, "id"), time = "2h" });
        Text(await CallAsync(agent.Http, "list_work_logs", new { key }), "total").ShouldBe("2h");

        (await CallAsync(agent.Http, "unwatch_issue", new { key })).GetProperty("watchedByMe").GetBoolean().ShouldBeFalse();
        (await CallAsync(agent.Http, "watch_issue", new { key })).GetProperty("watchedByMe").GetBoolean().ShouldBeTrue();

        var activity = await CallAsync(agent.Http, "get_issue_activity", new { key });
        activity.GetProperty("activity").EnumerateArray().Select(item => Text(item, "field")).ShouldContain("status");
    }

    [Fact]
    public async Task Issues_move_between_sprints_and_show_on_the_backlog_and_board()
    {
        var agent = await AgentAsync();
        var key = agent.Space.Key;
        (await agent.Owner.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = agent.Space.Id, name = "Sprint 1" }, Token)).EnsureSuccessStatusCode();
        var first = Text(await CallAsync(agent.Http, "create_issue", new { space = key, title = "Planned", sprint = "Sprint 1" }), "key");
        var second = Text(await CallAsync(agent.Http, "create_issue", new { space = key, title = "Later" }), "key");

        var backlog = await CallAsync(agent.Http, "get_backlog", new { space = key });
        backlog.GetProperty("sprints")[0].GetProperty("issues")[0].GetProperty("key").GetString().ShouldBe(first);
        backlog.GetProperty("backlog")[0].GetProperty("key").GetString().ShouldBe(second);

        await CallAsync(agent.Http, "move_to_sprint", new { keys = new[] { second }, sprint = "sprint 1", before = first });
        var moved = await CallAsync(agent.Http, "get_backlog", new { space = key });
        moved.GetProperty("sprints")[0].GetProperty("issues").EnumerateArray().Select(item => Text(item, "key")).ShouldBe([second, first]);

        await CallAsync(agent.Http, "update_issue", new { key = first, sprint = "backlog" });
        Text(await CallAsync(agent.Http, "get_issue", new { key = second }), "sprint").ShouldStartWith("Sprint 1");

        var board = await CallAsync(agent.Http, "get_board", new { space = key });
        board.GetProperty("columns").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Read_only_tokens_and_outsiders_are_refused()
    {
        var agent = await AgentAsync(readOnly: true);
        (await CallAsync(agent.Http, "list_spaces", new { })).EnumerateArray().Select(item => Text(item, "key")).ShouldContain(agent.Space.Key);
        foreach (var (tool, arguments) in new (string, object)[]
        {
            ("create_issue", new { space = agent.Space.Key, title = "No" }),
            ("create_issues", new { space = agent.Space.Key, items = new[] { new { title = "No" } } }),
            ("update_issues", new { keys = new[] { "X-1" }, assignee = "me" }),
            ("move_to_sprint", new { keys = new[] { "X-1" }, sprint = "backlog" }),
            ("log_work", new { key = "X-1", time = "1h" }),
            ("watch_issue", new { key = "X-1" }),
        })
        {
            (await Should.ThrowAsync<ToolError>(() => CallAsync(agent.Http, tool, arguments))).Message.ShouldContain("read-only");
        }

        var owner = await AgentAsync();
        var key = Text(await CallAsync(owner.Http, "create_issue", new { space = owner.Space.Key, title = "Private" }), "key");
        var outsider = await AgentAsync();
        await Should.ThrowAsync<ToolError>(() => CallAsync(outsider.Http, "get_issue", new { key }));
        await Should.ThrowAsync<ToolError>(() => CallAsync(outsider.Http, "update_issue", new { key, title = "Mine" }));
        (await CallAsync(outsider.Http, "search_issues", new { text = "Private" })).GetProperty("total").GetInt32().ShouldBe(0);
    }
}
