using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PersonalTokenTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<CreatedPersonalTokenDto> CreateAsync(AuthClient client, PersonalTokenKind kind, bool readOnly = false)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/personal-token", new { name = "Script", kind, readOnly, expiresInDays = 30 }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<CreatedPersonalTokenDto>(Token))!;
    }

    private HttpClient Bearer(string token)
    {
        var http = fixture.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<(HttpStatusCode Status, string Body)> McpAsync(HttpClient http, string method, object parameters)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        var response = await http.SendAsync(request, Token);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task An_api_token_acts_as_its_owner_until_revoked()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var created = await CreateAsync(owner, PersonalTokenKind.Api);
        created.Token.ShouldStartWith("sb_api_");

        var listed = await owner.Http.GetStringAsync("/api/v1/personal-token", Token);
        listed.ShouldNotContain(created.Token);

        var http = Bearer(created.Token);
        var key = $"T{Random.Shared.Next(100000, 999999)}";
        (await http.PostAsJsonAsync("/api/v1/project", new { name = "By token", key, type = 0 }, Token)).EnsureSuccessStatusCode();
        var spaces = await http.GetFromJsonAsync<List<ProjectDto>>("/api/v1/project", Token);
        spaces!.ShouldContain(space => space.Key == key);

        (await http.GetAsync("/api/v1/personal-token", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await McpAsync(http, "tools/list", new { })).Status.ShouldBe(HttpStatusCode.Forbidden);

        var stranger = await fixture.CreateUserClientAsync(Token);
        (await stranger.Http.DeleteAsync($"/api/v1/personal-token/{created.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.Http.DeleteAsync($"/api/v1/personal-token/{created.Id}", Token)).EnsureSuccessStatusCode();
        (await http.GetAsync("/api/v1/project", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_read_only_token_cannot_write()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var http = Bearer((await CreateAsync(owner, PersonalTokenKind.Api, readOnly: true)).Token);

        (await http.GetAsync("/api/v1/project", Token)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("/api/v1/project", new { name = "No", key = "NOPE", type = 0 }, Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_unknown_token_is_refused()
    {
        (await Bearer("sb_api_nothing").GetAsync("/api/v1/project", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await McpAsync(fixture.Factory.CreateClient(), "tools/list", new { })).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_mcp_token_lists_and_runs_tools_within_the_owners_rights()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var key = $"M{Random.Shared.Next(100000, 999999)}";
        (await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Mcp space", key, type = 0 }, Token)).EnsureSuccessStatusCode();
        var http = Bearer((await CreateAsync(owner, PersonalTokenKind.Mcp)).Token);

        var tools = await McpAsync(http, "tools/list", new { });
        tools.Status.ShouldBe(HttpStatusCode.OK, tools.Body);
        tools.Body.ShouldContain("create_issue");
        tools.Body.ShouldContain("transition_issue");

        var created = await McpAsync(http, "tools/call", new { name = "create_issue", arguments = new { space = key, title = "From an agent" } });
        created.Body.ShouldContain($"{key}-1");
        var found = await McpAsync(http, "tools/call", new { name = "search_issues", arguments = new { space = key } });
        found.Body.ShouldContain("From an agent");

        var moved = await McpAsync(http, "tools/call", new { name = "transition_issue", arguments = new { key = $"{key}-1", status = "Done" } });
        moved.Body.ShouldNotContain("\"isError\":true", Case.Insensitive, moved.Body);

        (await http.GetAsync("/api/v1/project", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var readOnly = Bearer((await CreateAsync(owner, PersonalTokenKind.Mcp, readOnly: true)).Token);
        var refused = await McpAsync(readOnly, "tools/call", new { name = "add_comment", arguments = new { key = $"{key}-1", body = "No" } });
        refused.Body.ShouldContain("read-only");

        var stranger = await fixture.CreateUserClientAsync(Token);
        var outsider = Bearer((await CreateAsync(stranger, PersonalTokenKind.Mcp)).Token);
        var hidden = await McpAsync(outsider, "tools/call", new { name = "get_issue", arguments = new { key = $"{key}-1" } });
        hidden.Body.ShouldContain("\"isError\":true");
    }
}
