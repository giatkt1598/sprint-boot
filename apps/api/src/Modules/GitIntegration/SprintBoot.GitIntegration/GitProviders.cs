using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace SprintBoot.GitIntegration;

public record GitRepoInfo(string ExternalId, string FullName, string DefaultBranch, bool IsPrivate, string WebUrl);

public record GitCommitInfo(string Sha, string Message, string? AuthorName, DateTime CommittedAt, string Url);

public abstract record GitEvent(GitRepoInfo Repository);

public record GitPushEvent(GitRepoInfo Repository, string Branch, IReadOnlyList<GitCommitInfo> Commits) : GitEvent(Repository);

public record GitBranchEvent(GitRepoInfo Repository, string Branch, bool Deleted) : GitEvent(Repository);

public record GitPullRequestEvent(
    GitRepoInfo Repository, int Number, string Title, string? Body, GitPullRequestState State, string Url, string HeadSha, string HeadBranch)
    : GitEvent(Repository);

public record GitStatusEvent(GitRepoInfo Repository, string Sha, string Name, GitCiState State, string? Url) : GitEvent(Repository);

/// <summary>The API token or credential was refused; the connection needs an admin.</summary>
public class GitAuthException(string message) : Exception(message);

/// <summary>The provider failed or could not be reached.</summary>
public class GitProviderException(string message, Exception? inner = null) : Exception(message, inner);

public class GitBranchExistsException() : Exception("The branch already exists.");

/// <summary>A resolved way to call a provider. Adapters never see stored secrets directly.</summary>
public record GitAccess(string BaseUrl, string Token);

/// <summary>
/// Everything that differs between providers. Designed from both the GitHub and Gitea APIs,
/// so business code never branches on the provider type.
/// </summary>
public interface IGitProvider
{
    GitProviderType Type { get; }

    /// <summary>True when the provider delivers events itself and no per-repository hook is needed.</summary>
    bool HasConnectionWideWebhook { get; }

    Task<IReadOnlyList<GitRepoInfo>> ListRepositoriesAsync(GitAccess access);

    Task<IReadOnlyList<string>> ListBranchesAsync(GitAccess access, string fullName);

    Task CreateBranchAsync(GitAccess access, string fullName, string name, string baseBranch);

    /// <summary>Returns the provider's hook id.</summary>
    Task<string> RegisterWebhookAsync(GitAccess access, string fullName, string url, string secret);

    Task CheckAccessAsync(GitAccess access);

    string BranchUrl(GitRepoInfo repository, string branch);

    /// <summary>Null for events the product has no use for.</summary>
    GitEvent? Parse(string eventName, string payload);
}

public static class GitHttp
{
    public const string ClientName = "git";

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, AuthenticationHeaderValue? auth, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = auth;
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("SprintBoot");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            return await client.SendAsync(request);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            throw new GitProviderException($"Could not reach {new Uri(url).Host}.", failure);
        }
    }

    public static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new GitAuthException("The provider refused the credentials.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GitProviderException($"The provider answered {(int)response.StatusCode}.");
        }

        await Task.CompletedTask;
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        await EnsureAsync(response);
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            return document.RootElement.Clone();
        }
        catch (JsonException failure)
        {
            throw new GitProviderException("The provider sent something that is not JSON.", failure);
        }
    }

    public static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    public static bool SignatureMatches(string body, string secret, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Hex(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(signature));
    }
}

/// <summary>GitHub and Gitea share most webhook fields; differences stay in the adapters.</summary>
public static class GitPayloads
{
    public static GitRepoInfo Repository(JsonElement repository) => new(
        repository.GetProperty("id").GetRawText(),
        repository.GetProperty("full_name").GetString()!,
        Text(repository, "default_branch") ?? "main",
        repository.TryGetProperty("private", out var isPrivate) && isPrivate.GetBoolean(),
        repository.GetProperty("html_url").GetString()!);

    public static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static GitEvent? Parse(string eventName, JsonElement root)
    {
        if (!root.TryGetProperty("repository", out var repositoryElement))
        {
            return null;
        }

        var repository = Repository(repositoryElement);
        return eventName switch
        {
            "push" => Push(repository, root),
            "create" or "delete" => Text(root, "ref_type") == "branch"
                ? new GitBranchEvent(repository, root.GetProperty("ref").GetString()!, eventName == "delete")
                : null,
            "pull_request" => PullRequest(repository, root),
            "status" => new GitStatusEvent(
                repository,
                root.GetProperty("sha").GetString()!,
                Text(root, "context") ?? "status",
                FromStatus(Text(root, "state")),
                Text(root, "target_url")),
            _ => null,
        };
    }

    private static GitEvent? Push(GitRepoInfo repository, JsonElement root)
    {
        var reference = root.GetProperty("ref").GetString()!;
        const string heads = "refs/heads/";
        if (!reference.StartsWith(heads, StringComparison.Ordinal))
        {
            return null;
        }

        var branch = reference[heads.Length..];
        if (root.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True)
        {
            return new GitBranchEvent(repository, branch, Deleted: true);
        }

        var commits = root.GetProperty("commits").ValueKind == JsonValueKind.Array
            ? root.GetProperty("commits").EnumerateArray().Select(commit => new GitCommitInfo(
                commit.GetProperty("id").GetString()!,
                commit.GetProperty("message").GetString() ?? string.Empty,
                commit.TryGetProperty("author", out var author) ? Text(author, "name") : null,
                commit.GetProperty("timestamp").GetDateTimeOffset().UtcDateTime,
                commit.GetProperty("url").GetString()!)).ToList()
            : [];
        return new GitPushEvent(repository, branch, commits);
    }

    private static GitPullRequestEvent PullRequest(GitRepoInfo repository, JsonElement root)
    {
        var pull = root.GetProperty("pull_request");
        var merged = pull.TryGetProperty("merged", out var flag) && flag.ValueKind == JsonValueKind.True;
        var state = merged ? GitPullRequestState.Merged
            : Text(pull, "state") == "open" ? GitPullRequestState.Open : GitPullRequestState.Closed;
        var head = pull.GetProperty("head");
        return new GitPullRequestEvent(
            repository,
            root.GetProperty("number").GetInt32(),
            pull.GetProperty("title").GetString()!,
            Text(pull, "body"),
            state,
            pull.GetProperty("html_url").GetString()!,
            head.GetProperty("sha").GetString()!,
            head.GetProperty("ref").GetString()!);
    }

    private static GitCiState FromStatus(string? state) => state switch
    {
        "success" => GitCiState.Success,
        "pending" => GitCiState.Pending,
        _ => GitCiState.Failure,
    };
}

[ExposeServices(typeof(IGitProvider))]
public class GiteaProvider(IHttpClientFactory clients) : IGitProvider, ITransientDependency
{
    private const int PageSize = 50;

    public GitProviderType Type => GitProviderType.Gitea;

    public bool HasConnectionWideWebhook => false;

    private static AuthenticationHeaderValue Auth(GitAccess access) => new("token", access.Token);

    private static string Api(GitAccess access, string path) => $"{access.BaseUrl}/api/v1{path}";

    private Task<HttpResponseMessage> SendAsync(GitAccess access, HttpMethod method, string path, object? body = null) =>
        GitHttp.SendAsync(clients.CreateClient(GitHttp.ClientName), method, Api(access, path), Auth(access), body);

    public async Task CheckAccessAsync(GitAccess access) =>
        await GitHttp.EnsureAsync(await SendAsync(access, HttpMethod.Get, "/user"));

    public async Task<IReadOnlyList<GitRepoInfo>> ListRepositoriesAsync(GitAccess access)
    {
        var result = new List<GitRepoInfo>();
        for (var page = 1; ; page++)
        {
            var rows = await GitHttp.ReadAsync(await SendAsync(access, HttpMethod.Get, $"/user/repos?limit={PageSize}&page={page}"));
            result.AddRange(rows.EnumerateArray().Select(GitPayloads.Repository));
            if (rows.GetArrayLength() < PageSize)
            {
                return result;
            }
        }
    }

    public async Task<IReadOnlyList<string>> ListBranchesAsync(GitAccess access, string fullName)
    {
        var rows = await GitHttp.ReadAsync(await SendAsync(access, HttpMethod.Get, $"/repos/{fullName}/branches?limit={PageSize}"));
        return [.. rows.EnumerateArray().Select(row => row.GetProperty("name").GetString()!)];
    }

    public async Task CreateBranchAsync(GitAccess access, string fullName, string name, string baseBranch)
    {
        var response = await SendAsync(access, HttpMethod.Post, $"/repos/{fullName}/branches", new { new_branch_name = name, old_branch_name = baseBranch });
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new GitBranchExistsException();
        }

        await GitHttp.EnsureAsync(response);
    }

    public async Task<string> RegisterWebhookAsync(GitAccess access, string fullName, string url, string secret)
    {
        var hook = await GitHttp.ReadAsync(await SendAsync(access, HttpMethod.Post, $"/repos/{fullName}/hooks", new
        {
            type = "gitea",
            active = true,
            events = new[] { "push", "create", "delete", "pull_request", "status" },
            branch_filter = "*",
            config = new { url, content_type = "json", secret },
        }));
        return hook.GetProperty("id").GetRawText();
    }

    public string BranchUrl(GitRepoInfo repository, string branch) => $"{repository.WebUrl}/src/branch/{branch}";

    public GitEvent? Parse(string eventName, string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return GitPayloads.Parse(eventName, document.RootElement);
    }
}

[ExposeServices(typeof(IGitProvider))]
public class GitHubProvider(IHttpClientFactory clients, IConfiguration configuration) : IGitProvider, ITransientDependency
{
    private const int PageSize = 100;

    public GitProviderType Type => GitProviderType.GitHub;

    public bool HasConnectionWideWebhook => true;

    private string ApiUrl => (configuration["Git:GitHub:ApiUrl"] ?? "https://api.github.com").TrimEnd('/');

    private static AuthenticationHeaderValue Auth(GitAccess access) => new("Bearer", access.Token);

    private Task<HttpResponseMessage> SendAsync(GitAccess access, HttpMethod method, string path, object? body = null) =>
        GitHttp.SendAsync(clients.CreateClient(GitHttp.ClientName), method, ApiUrl + path, Auth(access), body);

    public async Task CheckAccessAsync(GitAccess access) =>
        await GitHttp.EnsureAsync(await SendAsync(access, HttpMethod.Get, "/installation/repositories?per_page=1"));

    public async Task<IReadOnlyList<GitRepoInfo>> ListRepositoriesAsync(GitAccess access)
    {
        var result = new List<GitRepoInfo>();
        for (var page = 1; ; page++)
        {
            var body = await GitHttp.ReadAsync(await SendAsync(access, HttpMethod.Get, $"/installation/repositories?per_page={PageSize}&page={page}"));
            var rows = body.GetProperty("repositories");
            result.AddRange(rows.EnumerateArray().Select(GitPayloads.Repository));
            if (rows.GetArrayLength() < PageSize)
            {
                return result;
            }
        }
    }

    public async Task<IReadOnlyList<string>> ListBranchesAsync(GitAccess access, string fullName)
    {
        var rows = await GitHttp.ReadAsync(await SendAsync(access, HttpMethod.Get, $"/repos/{fullName}/branches?per_page={PageSize}"));
        return [.. rows.EnumerateArray().Select(row => row.GetProperty("name").GetString()!)];
    }

    public async Task CreateBranchAsync(GitAccess access, string fullName, string name, string baseBranch)
    {
        var source = await GitHttp.ReadAsync(await SendAsync(access, HttpMethod.Get, $"/repos/{fullName}/git/ref/heads/{baseBranch}"));
        var sha = source.GetProperty("object").GetProperty("sha").GetString();
        var response = await SendAsync(access, HttpMethod.Post, $"/repos/{fullName}/git/refs", new { @ref = $"refs/heads/{name}", sha });
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            throw new GitBranchExistsException();
        }

        await GitHttp.EnsureAsync(response);
    }

    public Task<string> RegisterWebhookAsync(GitAccess access, string fullName, string url, string secret) =>
        throw new NotSupportedException("A GitHub App receives events for every installed repository.");

    public string BranchUrl(GitRepoInfo repository, string branch) => $"{repository.WebUrl}/tree/{branch}";

    public GitEvent? Parse(string eventName, string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return eventName == "check_run" ? CheckRun(root) : GitPayloads.Parse(eventName, root);
    }

    private static GitStatusEvent CheckRun(JsonElement root)
    {
        var run = root.GetProperty("check_run");
        var conclusion = GitPayloads.Text(run, "conclusion");
        var state = GitPayloads.Text(run, "status") switch
        {
            "completed" => conclusion is "success" or "neutral" or "skipped" ? GitCiState.Success : GitCiState.Failure,
            "in_progress" => GitCiState.Running,
            _ => GitCiState.Pending,
        };
        return new GitStatusEvent(
            GitPayloads.Repository(root.GetProperty("repository")),
            run.GetProperty("head_sha").GetString()!,
            run.GetProperty("name").GetString()!,
            state,
            GitPayloads.Text(run, "html_url"));
    }
}
