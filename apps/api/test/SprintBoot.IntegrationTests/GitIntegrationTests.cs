using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SprintBoot.GitIntegration;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Every business rule runs against both providers, as the acceptance criteria ask.</summary>
[Collection(ApiCollection.Name)]
public class GitIntegrationTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Harness(GitProviderType Provider, GitConnectionDto Connection, GitRepositoryDto Repository, string? Secret);

    private static HttpClient Anonymous(ApiFixture fixture) => fixture.Factory.CreateClient();

    private Task<Harness> ConnectAsync(AuthClient admin, ProjectDto project, GitProviderType provider, string baseUrl = FakeGitServer.GiteaUrl) =>
        ConnectAsync(admin, project.Id, provider, baseUrl);

    private async Task<Harness> ConnectAsync(AuthClient admin, Guid projectId, GitProviderType provider, string baseUrl)
    {
        var response = await admin.Http.PostAsJsonAsync("/api/v1/git-connection", new
        {
            projectId,
            name = $"conn-{Guid.NewGuid():N}",
            providerType = provider,
            baseUrl,
            authType = provider == GitProviderType.Gitea ? GitAuthType.AccessToken : GitAuthType.GitHubApp,
            accessToken = FakeGitServer.ValidToken,
        }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        var connection = (await response.Content.ReadFromJsonAsync<GitConnectionDto>(Token))!;

        if (provider == GitProviderType.GitHub)
        {
            await fixture.InstallGitHubAppAsync(connection.Id, Random.Shared.NextInt64(1, long.MaxValue));
            connection = (await (await admin.Http.PostAsync($"/api/v1/git-connection/{connection.Id}/sync", null, Token)).Content.ReadFromJsonAsync<GitConnectionDto>(Token))!;
        }

        var repositories = await admin.Http.GetFromJsonAsync<List<GitRepositoryDto>>($"/api/v1/git-connection/{connection.Id}/repositories", Token);
        var repository = repositories!.Single();
        string? secret = null;
        if (provider == GitProviderType.Gitea)
        {
            secret = (await admin.Http.GetFromJsonAsync<GitWebhookSetupDto>($"/api/v1/git-connection/webhook-setup/{repository.Id}", Token))!.Secret;
        }

        return new Harness(provider, connection, repository, secret);
    }

    private async Task<long> InstallationOfAsync(Guid connectionId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        return (await scope.ServiceProvider.GetRequiredService<IRepository<GitConnection, Guid>>().GetAsync(connectionId)).InstallationId!.Value;
    }

    private async Task<HttpResponseMessage> SendAsync(Harness harness, string eventName, object payload, string? delivery = null, bool badSignature = false)
    {
        var fields = JsonSerializer.SerializeToNode(payload)!.AsObject();
        fields["repository"] = JsonSerializer.SerializeToNode(FakeGitServer.Repository);
        var github = harness.Provider == GitProviderType.GitHub;
        if (github)
        {
            fields["installation"] = JsonSerializer.SerializeToNode(new { id = await InstallationOfAsync(harness.Connection.Id) });
        }

        var body = fields.ToJsonString();
        var secret = github ? ApiFixture.GitHubWebhookSecret : harness.Secret!;
        var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(badSignature ? "wrong" : secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, github ? "/api/v1/git/webhooks/github" : $"/api/v1/git/webhooks/gitea/{harness.Repository.Id}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        var prefix = github ? "X-GitHub" : "X-Gitea";
        request.Headers.Add($"{prefix}-Delivery", delivery ?? Guid.NewGuid().ToString());
        request.Headers.Add($"{prefix}-Event", eventName);
        request.Headers.Add(github ? "X-Hub-Signature-256" : "X-Gitea-Signature", github ? $"sha256={hash}" : hash);
        return await Anonymous(fixture).SendAsync(request, Token);
    }

    private static object Push(string branch, string sha, string message) => new
    {
        @ref = $"refs/heads/{branch}",
        commits = new[]
        {
            new { id = sha, message, url = $"http://git.fake/{sha}", timestamp = DateTime.UtcNow, author = new { name = "Dev" } },
        },
    };

    private async Task<(AuthClient Client, ProjectDto Project, IssueDto Issue)> IssueAsync()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var key = $"G{Random.Shared.Next(100000, 999999)}";
        var project = (await (await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Git", key, type = 0 }, Token)).Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{project.Id}", Token);
        var typeId = types!.First(type => type.Hierarchy == IssueHierarchy.Standard).Id;
        var created = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId, title = "Đăng nhập" }, Token);
        return (client, project, (await created.Content.ReadFromJsonAsync<IssueDto>(Token))!);
    }

    private static Task<DevelopmentDto?> DevelopmentAsync(AuthClient client, IssueDto issue) =>
        client.Http.GetFromJsonAsync<DevelopmentDto>($"/api/v1/git-issue/development/{issue.Id}", Token);

    private static async Task<DevelopmentDto> WaitAsync(AuthClient client, IssueDto issue, Func<DevelopmentDto, bool> done)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var development = (await DevelopmentAsync(client, issue))!;
            if (done(development))
            {
                return development;
            }

            await Task.Delay(200, Token);
        }

        throw new TimeoutException("The webhook was not processed.");
    }

    private static object CreateBranchBody(GitRepositoryDto repository, string name) => new { repositoryId = repository.Id, baseBranch = "main", name };

    [Theory]
    [InlineData(GitProviderType.GitHub)]
    [InlineData(GitProviderType.Gitea)]
    public async Task Connecting_syncs_repositories_and_never_returns_the_token(GitProviderType provider)
    {
        var (admin, project, _) = await IssueAsync();

        var harness = await ConnectAsync(admin, project, provider);

        harness.Connection.Status.ShouldBe(GitConnectionStatus.Connected);
        harness.Repository.FullName.ShouldBe("acme/web");
        var body = await admin.Http.GetStringAsync($"/api/v1/git-connection?projectId={project.Id}", Token);
        body.ShouldNotContain(FakeGitServer.ValidToken);
    }

    [Theory]
    [InlineData(GitProviderType.GitHub)]
    [InlineData(GitProviderType.Gitea)]
    public async Task A_wrong_signature_is_refused_and_writes_nothing(GitProviderType provider)
    {
        var (client, project, issue) = await IssueAsync();
        var harness = await ConnectAsync(client, project, provider);

        var response = await SendAsync(harness, "push", Push("main", Guid.NewGuid().ToString("N"), $"{issue.Key} tampered"), badSignature: true);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await Task.Delay(1000, Token);
        (await DevelopmentAsync(client, issue))!.Commits.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(GitProviderType.GitHub)]
    [InlineData(GitProviderType.Gitea)]
    public async Task A_commit_links_once_even_when_the_delivery_repeats(GitProviderType provider)
    {
        var (client, project, issue) = await IssueAsync();
        var harness = await ConnectAsync(client, project, provider);
        var sha = Guid.NewGuid().ToString("N");
        var delivery = Guid.NewGuid().ToString();

        (await SendAsync(harness, "push", Push("main", sha, $"{issue.Key} works"), delivery)).EnsureSuccessStatusCode();
        (await SendAsync(harness, "push", Push("main", sha, $"{issue.Key} works"), delivery)).EnsureSuccessStatusCode();

        var development = await WaitAsync(client, issue, item => item.Commits.Count > 0);
        await Task.Delay(1000, Token);
        (await DevelopmentAsync(client, issue))!.Commits.Count.ShouldBe(1);
        development.Commits.Single().Sha.ShouldBe(sha);
    }

    [Theory]
    [InlineData(GitProviderType.GitHub)]
    [InlineData(GitProviderType.Gitea)]
    public async Task A_key_without_an_issue_links_nothing(GitProviderType provider)
    {
        var (client, project, issue) = await IssueAsync();
        var harness = await ConnectAsync(client, project, provider);
        var marker = Guid.NewGuid().ToString("N");

        (await SendAsync(harness, "push", Push("main", Guid.NewGuid().ToString("N"), $"{project.Key}-99999 nothing {marker}"))).EnsureSuccessStatusCode();
        (await SendAsync(harness, "push", Push("main", marker, $"{issue.Key} marker"))).EnsureSuccessStatusCode();

        var development = await WaitAsync(client, issue, item => item.Commits.Count > 0);
        development.Commits.ShouldAllBe(commit => commit.Sha == marker);
    }

    [Theory]
    [InlineData(GitProviderType.GitHub)]
    [InlineData(GitProviderType.Gitea)]
    public async Task Pull_requests_and_ci_status_update_the_issue(GitProviderType provider)
    {
        var (client, project, issue) = await IssueAsync();
        var harness = await ConnectAsync(client, project, provider);
        var sha = Guid.NewGuid().ToString("N");
        var number = Random.Shared.Next(1, 100000);
        object Pull(string state, bool merged) => new
        {
            number,
            pull_request = new { title = $"{issue.Key} work", body = "body", state, merged, html_url = $"http://git.fake/pr/{number}", head = new { sha, @ref = "topic" } },
        };

        (await SendAsync(harness, "pull_request", Pull("open", merged: false))).EnsureSuccessStatusCode();
        await WaitAsync(client, issue, item => item.PullRequests.Any(pull => pull.State == GitPullRequestState.Open));

        (await SendAsync(harness, "status", new { sha, context = "ci/build", state = "pending", target_url = "http://ci/1" })).EnsureSuccessStatusCode();
        await WaitAsync(client, issue, item => item.Builds.FirstOrDefault()?.State == GitCiState.Pending);
        (await SendAsync(harness, "status", new { sha, context = "ci/build", state = "success", target_url = "http://ci/1" })).EnsureSuccessStatusCode();
        var built = await WaitAsync(client, issue, item => item.Builds.FirstOrDefault()?.State == GitCiState.Success);
        built.Builds[0].Url.ShouldBe("http://ci/1");

        (await SendAsync(harness, "pull_request", Pull("closed", merged: true))).EnsureSuccessStatusCode();
        await WaitAsync(client, issue, item => item.PullRequests.Single().State == GitPullRequestState.Merged);

        using var scope = fixture.Factory.Services.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        (await scope.ServiceProvider.GetRequiredService<IIssueGitFacts>().HasPullRequestAsync(issue.Id)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(GitProviderType.GitHub)]
    [InlineData(GitProviderType.Gitea)]
    public async Task Creating_a_branch_links_it_and_a_taken_name_is_reported(GitProviderType provider)
    {
        var (client, project, issue) = await IssueAsync();
        var harness = await ConnectAsync(client, project, provider);

        var options = await client.Http.GetFromJsonAsync<BranchOptionsDto>($"/api/v1/git-issue/branch-options/{issue.Id}", Token);
        options!.SuggestedName.ShouldBe($"feature/{issue.Key}-dang-nhap");
        var name = $"{options.SuggestedName}-{Guid.NewGuid():N}";

        var created = await client.Http.PostAsJsonAsync($"/api/v1/git-issue/branch/{issue.Id}", CreateBranchBody(harness.Repository, name), Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        (await DevelopmentAsync(client, issue))!.Branches.Single().Name.ShouldBe(name);

        var again = await client.Http.PostAsJsonAsync($"/api/v1/git-issue/branch/{issue.Id}", CreateBranchBody(harness.Repository, name), Token);
        (await again.Content.ReadAsStringAsync(Token)).ShouldContain(GitErrors.BranchExists);
    }

    [Fact]
    public async Task Only_people_who_can_edit_the_issue_create_branches()
    {
        var (client, project, issue) = await IssueAsync();
        var harness = await ConnectAsync(client, project, GitProviderType.Gitea);
        var outsider = await fixture.CreateUserClientAsync(Token);

        var response = await outsider.Http.PostAsJsonAsync($"/api/v1/git-issue/branch/{issue.Id}", CreateBranchBody(harness.Repository, "x"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revoked_token_flags_the_connection_and_a_provider_outage_leaves_the_issue_page_working()
    {
        var (client, project, issue) = await IssueAsync();
        var harness = await ConnectAsync(client, project, GitProviderType.Gitea);

        var update = await client.Http.PutAsJsonAsync($"/api/v1/git-connection/{harness.Connection.Id}", new
        {
            name = harness.Connection.Name,
            authType = GitAuthType.AccessToken,
            accessToken = "revoked",
        }, Token);
        var flagged = (await update.Content.ReadFromJsonAsync<GitConnectionDto>(Token))!;
        flagged.Status.ShouldBe(GitConnectionStatus.NeedsAttention);
        flagged.Problem.ShouldBe(GitConnectionProblem.TokenRevoked);

        FakeGitServer.Instance.Down = true;
        try
        {
            (await client.Http.GetAsync($"/api/v1/git-issue/development/{issue.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
            var branch = await client.Http.PostAsJsonAsync($"/api/v1/git-issue/branch/{issue.Id}", CreateBranchBody(harness.Repository, "down"), Token);
            (await branch.Content.ReadAsStringAsync(Token)).ShouldContain(GitErrors.ProviderUnavailable);
        }
        finally
        {
            FakeGitServer.Instance.Down = false;
        }
    }

    [Fact]
    public async Task Connections_belong_to_a_space()
    {
        var (owner, project, _) = await IssueAsync();
        var harness = await ConnectAsync(owner, project, GitProviderType.Gitea);
        var outsider = await fixture.CreateUserClientAsync(Token);

        (await outsider.Http.GetAsync($"/api/v1/git-connection?projectId={project.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.Http.GetAsync($"/api/v1/git-connection/{harness.Connection.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var (other, _, otherIssue) = await IssueAsync();
        (await SendAsync(harness, "push", Push("main", Guid.NewGuid().ToString("N"), $"{otherIssue.Key} from another space"))).EnsureSuccessStatusCode();
        await Task.Delay(1500, Token);
        (await DevelopmentAsync(other, otherIssue))!.Commits.ShouldBeEmpty();
        (await DevelopmentAsync(other, otherIssue))!.CanCreateBranch.ShouldBeFalse();
    }

    [Fact]
    public async Task An_organization_address_limits_the_connection_to_that_organization()
    {
        var (client, project, _) = await IssueAsync();

        var inside = await ConnectAsync(client, project, GitProviderType.Gitea, $"{FakeGitServer.GiteaUrl}/acme");
        inside.Connection.BaseUrl.ShouldBe($"{FakeGitServer.GiteaUrl}/acme");
        inside.Repository.FullName.ShouldBe("acme/web");

        var response = await client.Http.PostAsJsonAsync("/api/v1/git-connection", new
        {
            projectId = project.Id,
            name = "Elsewhere",
            providerType = GitProviderType.Gitea,
            baseUrl = $"{FakeGitServer.GiteaUrl}/acm",
            authType = GitAuthType.AccessToken,
            accessToken = FakeGitServer.ValidToken,
        }, Token);
        var outside = (await response.Content.ReadFromJsonAsync<GitConnectionDto>(Token))!;
        (await client.Http.GetFromJsonAsync<List<GitRepositoryDto>>($"/api/v1/git-connection/{outside.Id}/repositories", Token))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_connection_without_a_space_stays_out_of_every_space_until_moved()
    {
        var (owner, project, issue) = await IssueAsync();
        var harness = await ConnectAsync(owner, project, GitProviderType.Gitea);
        await fixture.DetachGitConnectionAsync(harness.Connection.Id);

        (await owner.Http.GetFromJsonAsync<List<GitConnectionDto>>($"/api/v1/git-connection?projectId={project.Id}", Token))!.ShouldBeEmpty();
        (await DevelopmentAsync(owner, issue))!.CanCreateBranch.ShouldBeFalse();

        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var moved = await admin.Http.PostAsJsonAsync($"/api/v1/git-connection/{harness.Connection.Id}/assign", new { projectId = project.Id }, Token);
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(Token));
        (await DevelopmentAsync(owner, issue))!.CanCreateBranch.ShouldBeTrue();
    }
}
