using System.Net;
using System.Text;
using System.Text.Json;

namespace SprintBoot.IntegrationTests;

/// <summary>Speaks just enough of the GitHub and Gitea APIs for the adapters; test code decides what fails.</summary>
public sealed class FakeGitServer : HttpMessageHandler
{
    public const string GitHubApi = "http://github.fake";
    public const string GiteaUrl = "http://gitea.fake";
    public const string ValidToken = "good-token";
    public const int RepositoryId = 700;

    public static readonly FakeGitServer Instance = new();

    public static readonly object Repository = new
    {
        id = RepositoryId,
        full_name = "acme/web",
        default_branch = "main",
        @private = true,
        html_url = "http://git.fake/acme/web",
    };

    private readonly HashSet<string> _branches = ["main"];

    public bool Down { get; set; }

    public int PollCalls { get; set; }

    public List<object> Commits { get; } = [];

    public List<object> MainCommits { get; } = [];

    public List<object> Pulls { get; } = [];

    public List<object> Statuses { get; } = [];

    public void ResetPolling()
    {
        PollCalls = 0;
        Commits.Clear();
        MainCommits.Clear();
        Pulls.Clear();
        Statuses.Clear();
        _branches.Clear();
        _branches.Add("main");
    }

    public void AddBranch(string name) => _branches.Add(name);

    public void RemoveBranch(string name) => _branches.Remove(name);

    /// <summary>What both providers answer the same way when listing a repository's work.</summary>
    private Task<HttpResponseMessage>? Listing(HttpRequestMessage request, string path)
    {
        if (request.Method != HttpMethod.Get || !path.Contains("/repos/acme/web/", StringComparison.Ordinal))
        {
            return null;
        }

        if (path.Contains("/pulls?", StringComparison.Ordinal))
        {
            PollCalls++;
            return Reply(HttpStatusCode.OK, Pulls);
        }

        if (path.Contains("/check-runs", StringComparison.Ordinal))
        {
            return Reply(HttpStatusCode.OK, new { check_runs = Array.Empty<object>() });
        }

        if (path.EndsWith("/status", StringComparison.Ordinal))
        {
            return Reply(HttpStatusCode.OK, new { statuses = Statuses });
        }

        if (path.Contains("/commits?", StringComparison.Ordinal))
        {
            return Reply(HttpStatusCode.OK, path.Contains("sha=main", StringComparison.Ordinal) ? MainCommits : Commits);
        }

        return path.Contains("/branches?", StringComparison.Ordinal)
            ? Reply(HttpStatusCode.OK, _branches.Select(name => new { name }))
            : null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down)
        {
            throw new HttpRequestException("Connection refused");
        }

        var path = request.RequestUri!.PathAndQuery;
        var github = request.RequestUri.GetLeftPart(UriPartial.Authority) == GitHubApi;
        var auth = request.Headers.Authorization?.Parameter;
        if (!github && auth != ValidToken)
        {
            return Reply(HttpStatusCode.Unauthorized, new { });
        }

        return Listing(request, path) ?? (github ? GitHub(request, path) : Gitea(request, path));
    }

    private Task<HttpResponseMessage> GitHub(HttpRequestMessage request, string path)
    {
        if (path.Contains("/access_tokens", StringComparison.Ordinal))
        {
            return Reply(HttpStatusCode.Created, new { token = "installation-token", expires_at = DateTime.UtcNow.AddHours(1) });
        }

        if (path.StartsWith("/installation/repositories", StringComparison.Ordinal))
        {
            return Reply(HttpStatusCode.OK, new { repositories = new[] { Repository } });
        }

        if (path.Contains("/git/ref/heads/", StringComparison.Ordinal))
        {
            return Reply(HttpStatusCode.OK, new { @object = new { sha = "base-sha" } });
        }

        if (path.EndsWith("/git/refs", StringComparison.Ordinal))
        {
            var body = Body(request);
            return Create(body.GetProperty("ref").GetString()!["refs/heads/".Length..], HttpStatusCode.UnprocessableEntity);
        }

        return Reply(HttpStatusCode.OK, Array.Empty<object>());
    }

    private Task<HttpResponseMessage> Gitea(HttpRequestMessage request, string path)
    {
        if (path.StartsWith("/api/v1/user/repos", StringComparison.Ordinal))
        {
            return Reply(HttpStatusCode.OK, new[] { Repository });
        }

        if (path.EndsWith("/hooks", StringComparison.Ordinal))
        {
            return Reply(HttpStatusCode.Created, new { id = 5 });
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/branches", StringComparison.Ordinal))
        {
            return Create(Body(request).GetProperty("new_branch_name").GetString()!, HttpStatusCode.Conflict);
        }

        return Reply(HttpStatusCode.OK, new { });
    }

    private Task<HttpResponseMessage> Create(string name, HttpStatusCode taken) =>
        _branches.Add(name) ? Reply(HttpStatusCode.Created, new { }) : Reply(taken, new { });

    private static JsonElement Body(HttpRequestMessage request) =>
        JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;

    private static Task<HttpResponseMessage> Reply(HttpStatusCode status, object body) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        });
}
