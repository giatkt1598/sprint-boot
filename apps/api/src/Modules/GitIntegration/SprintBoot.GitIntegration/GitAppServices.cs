using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SprintBoot.Identity;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Timing;

namespace SprintBoot.GitIntegration;

[Authorize(SprintBootPermissions.ManageGit)]
public class GitConnectionAppService(
    IRepository<GitConnection, Guid> connections,
    IRepository<GitRepository, Guid> repositories,
    IRepository<GitBranch, Guid> branches,
    IRepository<GitCommit, Guid> commits,
    IRepository<GitPullRequest, Guid> pullRequests,
    IRepository<CiStatus, Guid> statuses,
    IRepository<IssueGitLink, Guid> links,
    GitAccessProvider access,
    GitSyncService sync,
    GitAuthState authState,
    IStringEncryptionService encryption,
    IConfiguration configuration)
    : ApplicationService, IGitConnectionAppService
{
    private const string GitHubHost = "github.com";

    public async Task<List<GitConnectionDto>> GetListAsync()
    {
        var list = await connections.GetListAsync();
        var repos = await repositories.GetListAsync();
        return [.. list.OrderBy(item => item.Name).Select(item => ToDto(item, repos))];
    }

    public async Task<GitConnectionDto> GetAsync(Guid id) => await ToDtoAsync(await connections.GetAsync(id));

    public async Task<GitConnectionDto> CreateAsync(SaveGitConnectionInput input)
    {
        var baseUrl = input.ProviderType == GitProviderType.GitHub ? GitHubHost : NormalizeUrl(input.BaseUrl);
        var authType = input.ProviderType == GitProviderType.GitHub ? GitAuthType.GitHubApp : input.AuthType;
        var connection = new GitConnection(GuidGenerator.Create(), input.Name, input.ProviderType, baseUrl, authType);
        ApplyCredentials(connection, input);
        await connections.InsertAsync(connection, autoSave: true);
        if (authType == GitAuthType.AccessToken)
        {
            await sync.SyncAsync(connection);
        }

        return await ToDtoAsync(connection);
    }

    public async Task<GitConnectionDto> UpdateAsync(Guid id, SaveGitConnectionInput input)
    {
        var connection = await connections.GetAsync(id);
        connection.Name = input.Name;
        if (connection.ProviderType == GitProviderType.Gitea)
        {
            connection.AuthType = input.AuthType;
            ApplyCredentials(connection, input);
        }

        await connections.UpdateAsync(connection, autoSave: true);
        if (connection.AuthType == GitAuthType.AccessToken || connection.Status != GitConnectionStatus.Pending)
        {
            await sync.SyncAsync(connection);
        }

        return await ToDtoAsync(connection);
    }

    public async Task DeleteAsync(Guid id)
    {
        var ids = (await repositories.GetListAsync(item => item.ConnectionId == id)).Select(item => item.Id).ToList();
        var branchIds = (await branches.GetListAsync(item => ids.Contains(item.RepositoryId))).Select(item => item.Id).ToList();
        var commitIds = (await commits.GetListAsync(item => ids.Contains(item.RepositoryId))).Select(item => item.Id).ToList();
        var pullIds = (await pullRequests.GetListAsync(item => ids.Contains(item.RepositoryId))).Select(item => item.Id).ToList();
        await links.DeleteAsync(item =>
            (item.TargetType == GitLinkTarget.Branch && branchIds.Contains(item.TargetId))
            || (item.TargetType == GitLinkTarget.Commit && commitIds.Contains(item.TargetId))
            || (item.TargetType == GitLinkTarget.PullRequest && pullIds.Contains(item.TargetId)));
        await branches.DeleteAsync(item => ids.Contains(item.RepositoryId));
        await commits.DeleteAsync(item => ids.Contains(item.RepositoryId));
        await pullRequests.DeleteAsync(item => ids.Contains(item.RepositoryId));
        await statuses.DeleteAsync(item => ids.Contains(item.RepositoryId));
        await repositories.DeleteAsync(item => item.ConnectionId == id);
        await connections.DeleteAsync(id);
    }

    public async Task<GitTestResultDto> TestAsync(TestGitConnectionInput input)
    {
        try
        {
            GitAccess credential;
            GitProviderType type;
            if (input.ConnectionId is { } id)
            {
                var connection = await connections.GetAsync(id);
                type = connection.ProviderType;
                credential = string.IsNullOrEmpty(input.AccessToken)
                    ? await access.GetAsync(connection)
                    : new GitAccess(connection.BaseUrl, input.AccessToken);
            }
            else
            {
                type = GitProviderType.Gitea;
                credential = new GitAccess(NormalizeUrl(input.BaseUrl), input.AccessToken ?? string.Empty);
            }

            await access.Provider(type).CheckAccessAsync(credential);
            return new GitTestResultDto { Succeeded = true };
        }
        catch (Exception failure) when (failure is GitAuthException or GitProviderException)
        {
            return new GitTestResultDto { Succeeded = false };
        }
    }

    public async Task<GitAuthorizeUrlDto> GetAuthorizeUrlAsync(Guid id)
    {
        var connection = await connections.GetAsync(id);
        var state = authState.Issue(connection.Id);
        if (connection.ProviderType == GitProviderType.GitHub)
        {
            var slug = configuration["Git:GitHub:AppSlug"];
            return string.IsNullOrEmpty(slug)
                ? throw new BusinessException(GitErrors.GitHubNotConfigured)
                : new GitAuthorizeUrlDto { Url = $"https://github.com/apps/{slug}/installations/new?state={Uri.EscapeDataString(state)}" };
        }

        var clientId = access.Read(connection).ClientId ?? throw new BusinessException(GitErrors.NoCredentials);
        return new GitAuthorizeUrlDto
        {
            Url = $"{connection.BaseUrl}/login/oauth/authorize?client_id={Uri.EscapeDataString(clientId)}&response_type=code"
                + $"&redirect_uri={Uri.EscapeDataString(authState.OAuthRedirectUri)}&state={Uri.EscapeDataString(state)}",
        };
    }

    public async Task<List<GitRepositoryDto>> GetRepositoriesAsync(Guid id, string? search)
    {
        await connections.GetAsync(id);
        var list = await repositories.GetListAsync(item => item.ConnectionId == id);
        return
        [
            .. list
                .Where(item => string.IsNullOrWhiteSpace(search) || item.FullName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.FullName)
                .Select(item => new GitRepositoryDto
                {
                    Id = item.Id,
                    FullName = item.FullName,
                    IsPrivate = item.IsPrivate,
                    DefaultBranch = item.DefaultBranch,
                    WebhookState = item.WebhookState,
                    LastEventAt = item.LastEventAt,
                    IsAccessible = item.IsAccessible,
                }),
        ];
    }

    public async Task<GitConnectionDto> SyncAsync(Guid id)
    {
        var connection = await connections.GetAsync(id);
        await sync.SyncAsync(connection);
        return await ToDtoAsync(connection);
    }

    public async Task<GitWebhookSetupDto> GetWebhookSetupAsync(Guid repositoryId)
    {
        var repository = await repositories.GetAsync(repositoryId);
        if (repository.EncryptedWebhookSecret is null)
        {
            repository.EncryptedWebhookSecret = encryption.Encrypt(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            await repositories.UpdateAsync(repository);
        }

        return new GitWebhookSetupDto { Url = sync.WebhookUrl(repository), Secret = encryption.Decrypt(repository.EncryptedWebhookSecret)! };
    }

    private void ApplyCredentials(GitConnection connection, SaveGitConnectionInput input)
    {
        var credentials = access.Read(connection);
        if (!string.IsNullOrEmpty(input.AccessToken))
        {
            credentials.Token = input.AccessToken;
        }

        credentials.ClientId = string.IsNullOrEmpty(input.ClientId) ? credentials.ClientId : input.ClientId;
        credentials.ClientSecret = string.IsNullOrEmpty(input.ClientSecret) ? credentials.ClientSecret : input.ClientSecret;
        if (connection.AuthType == GitAuthType.AccessToken && credentials.Token is null)
        {
            throw new BusinessException(GitErrors.NoCredentials);
        }

        if (connection.AuthType == GitAuthType.OAuth2 && (credentials.ClientId is null || credentials.ClientSecret is null))
        {
            throw new BusinessException(GitErrors.NoCredentials);
        }

        access.Write(connection, credentials);
    }

    private static string NormalizeUrl(string? url)
    {
        var text = url?.Trim().TrimEnd('/');
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? text!
            : throw new BusinessException(GitErrors.InvalidConnection);
    }

    private async Task<GitConnectionDto> ToDtoAsync(GitConnection connection) =>
        ToDto(connection, await repositories.GetListAsync(item => item.ConnectionId == connection.Id));

    private static GitConnectionDto ToDto(GitConnection connection, List<GitRepository> repos)
    {
        var own = repos.Where(item => item.ConnectionId == connection.Id && item.IsAccessible).ToList();
        return new GitConnectionDto
        {
            Id = connection.Id,
            Name = connection.Name,
            ProviderType = connection.ProviderType,
            BaseUrl = connection.BaseUrl,
            AuthType = connection.AuthType,
            Status = connection.Status,
            Problem = connection.Problem,
            LastSyncAt = connection.LastSyncAt,
            RepositoryCount = own.Count,
            WebhookProblemCount = connection.ProviderType == GitProviderType.GitHub ? 0 : own.Count(item => item.WebhookState != GitWebhookState.Active),
        };
    }
}

[Authorize]
public class GitIssueAppService(
    IRepository<GitConnection, Guid> connections,
    IRepository<GitRepository, Guid> repositories,
    IRepository<GitBranch, Guid> branches,
    IRepository<GitCommit, Guid> commits,
    IRepository<GitPullRequest, Guid> pullRequests,
    IRepository<CiStatus, Guid> statuses,
    IRepository<IssueGitLink, Guid> links,
    IIssuePlanningGateway issues,
    IIssueFactsProvider facts,
    IProjectAccess projects,
    GitAccessProvider access,
    GitEventProcessor processor)
    : ApplicationService, IGitIssueAppService
{
    private const int RecentCommits = 10;

    public async Task<DevelopmentDto> GetDevelopmentAsync(Guid issueId)
    {
        await projects.CheckAsync(await issues.GetProjectIdAsync(issueId), ProjectPermissions.ViewIssues);
        var issueLinks = await links.GetListAsync(item => item.IssueId == issueId);
        var ids = issueLinks.Select(item => item.TargetId).ToList();

        var branchRows = await branches.GetListAsync(item => ids.Contains(item.Id));
        var commitRows = await commits.GetListAsync(item => ids.Contains(item.Id));
        var pullRows = await pullRequests.GetListAsync(item => ids.Contains(item.Id));

        var repositoryIds = branchRows.Select(item => item.RepositoryId).ToList();
        var repos = (await repositories.GetListAsync(item => repositoryIds.Contains(item.Id))).ToDictionary(item => item.Id);
        var connectionIds = repos.Values.Select(item => item.ConnectionId).ToList();
        var owners = (await connections.GetListAsync(item => connectionIds.Contains(item.Id))).ToDictionary(item => item.Id);

        var shas = commitRows.Select(item => item.Sha).Concat(pullRows.Select(item => item.HeadSha)).ToList();
        var build = (await statuses.GetListAsync(item => shas.Contains(item.Sha))).MaxBy(item => item.UpdatedAt);

        return new DevelopmentDto
        {
            CanCreateBranch = await repositories.AnyAsync(item => item.IsAccessible),
            Branches = [.. branchRows.OrderBy(item => item.Name).Select(item => new GitBranchRowDto
            {
                Name = item.Name,
                RepositoryName = repos[item.RepositoryId].FullName,
                Url = BranchUrl(owners[repos[item.RepositoryId].ConnectionId], repos[item.RepositoryId], item.Name),
            })],
            Commits = [.. commitRows.OrderByDescending(item => item.CommittedAt).Take(RecentCommits).Select(item => new GitCommitRowDto
            {
                Sha = item.Sha,
                Message = item.Message,
                AuthorName = item.AuthorName,
                CommittedAt = item.CommittedAt,
                Url = item.Url,
            })],
            PullRequests = [.. pullRows.OrderByDescending(item => item.Number).Select(item => new GitPullRequestRowDto
            {
                Number = item.Number,
                Title = item.Title,
                State = item.State,
                Url = item.Url,
            })],
            LatestBuild = build is null ? null : new GitBuildRowDto { Name = build.Name, State = build.State, Url = build.Url },
        };
    }

    public async Task<BranchOptionsDto> GetBranchOptionsAsync(Guid issueId)
    {
        var projectId = await issues.GetProjectIdAsync(issueId);
        await projects.CheckAsync(projectId, ProjectPermissions.EditIssues);
        var issue = await facts.FindAsync(issueId) ?? throw new BusinessException(GitErrors.InvalidConnection);

        var used = await UsedRepositoriesAsync(projectId);
        var repos = await repositories.GetListAsync(item => item.IsAccessible);
        var owners = (await connections.GetListAsync()).ToDictionary(item => item.Id);

        return new BranchOptionsDto
        {
            SuggestedName = IssueKeyScanner.BranchName(await projects.GetBranchTemplateAsync(projectId), issue.Key, issue.Title),
            Repositories = [.. repos
                .OrderByDescending(item => used.Contains(item.Id)).ThenBy(item => item.FullName)
                .Select(item => new BranchRepositoryDto
                {
                    Id = item.Id,
                    FullName = item.FullName,
                    ConnectionName = owners[item.ConnectionId].Name,
                    DefaultBranch = item.DefaultBranch,
                    UsedInSpace = used.Contains(item.Id),
                })],
        };
    }

    public async Task<List<string>> GetBranchesAsync(Guid issueId, Guid repositoryId)
    {
        await projects.CheckAsync(await issues.GetProjectIdAsync(issueId), ProjectPermissions.EditIssues);
        var (repository, connection) = await TargetAsync(repositoryId);
        return await CallAsync(async () =>
            (await access.Provider(connection.ProviderType).ListBranchesAsync(await access.GetAsync(connection), repository.FullName)).ToList());
    }

    public async Task<GitBranchRowDto> CreateBranchAsync(Guid issueId, CreateBranchInput input)
    {
        var projectId = await issues.GetProjectIdAsync(issueId);
        await projects.CheckAsync(projectId, ProjectPermissions.EditIssues);
        var (repository, connection) = await TargetAsync(input.RepositoryId);

        await CallAsync(async () =>
        {
            await access.Provider(connection.ProviderType)
                .CreateBranchAsync(await access.GetAsync(connection), repository.FullName, input.Name, input.BaseBranch);
            return true;
        });

        var branch = await branches.FirstOrDefaultAsync(item => item.RepositoryId == repository.Id && item.Name == input.Name)
            ?? await branches.InsertAsync(new GitBranch(GuidGenerator.Create(), repository.Id, input.Name), autoSave: true);
        await processor.LinkAsync(new IssueRef(issueId, projectId), GitLinkTarget.Branch, branch.Id);
        await issues.RecordActivityAsync(issueId, "branch", null, $"{repository.FullName}:{input.Name}");

        return new GitBranchRowDto
        {
            Name = branch.Name,
            RepositoryName = repository.FullName,
            Url = BranchUrl(connection, repository, branch.Name),
        };
    }

    private async Task<HashSet<Guid>> UsedRepositoriesAsync(Guid projectId)
    {
        var used = await links.GetListAsync(item => item.ProjectId == projectId);
        var ids = used.Select(item => item.TargetId).ToList();
        var fromBranches = await branches.GetListAsync(item => ids.Contains(item.Id));
        var fromCommits = await commits.GetListAsync(item => ids.Contains(item.Id));
        var fromPulls = await pullRequests.GetListAsync(item => ids.Contains(item.Id));
        return
        [
            .. fromBranches.Select(item => item.RepositoryId),
            .. fromCommits.Select(item => item.RepositoryId),
            .. fromPulls.Select(item => item.RepositoryId),
        ];
    }

    private async Task<(GitRepository Repository, GitConnection Connection)> TargetAsync(Guid repositoryId)
    {
        var repository = await repositories.GetAsync(repositoryId);
        return (repository, await connections.GetAsync(repository.ConnectionId));
    }

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (GitBranchExistsException)
        {
            throw new BusinessException(GitErrors.BranchExists);
        }
        catch (Exception failure) when (failure is GitAuthException or GitProviderException)
        {
            throw new BusinessException(GitErrors.ProviderUnavailable);
        }
    }

    private string BranchUrl(GitConnection connection, GitRepository repository, string name) =>
        access.Provider(connection.ProviderType).BranchUrl(
            new GitRepoInfo(repository.ExternalId, repository.FullName, repository.DefaultBranch, repository.IsPrivate, repository.WebUrl), name);
}

public class IssueGitFacts(IRepository<IssueGitLink, Guid> links) : IIssueGitFacts, ITransientDependency
{
    public Task<bool> HasPullRequestAsync(Guid issueId) =>
        links.AnyAsync(item => item.IssueId == issueId && item.TargetType == GitLinkTarget.PullRequest);
}

/// <summary>Signed, short-lived state so a provider redirect can only finish a setup an admin started.</summary>
public class GitAuthState(IDataProtectionProvider protection, GitSyncService sync) : ITransientDependency
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private ITimeLimitedDataProtector Protector => protection.CreateProtector("SprintBoot.Git.Setup").ToTimeLimitedDataProtector();

    public string OAuthRedirectUri => $"{sync.PublicUrl}/api/v1/git/oauth/callback";

    public string Issue(Guid connectionId) => Protector.Protect(connectionId.ToString(), Lifetime);

    public Guid? Read(string? state)
    {
        try
        {
            return string.IsNullOrEmpty(state) ? null : Guid.Parse(Protector.Unprotect(state));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Anonymous by nature: providers call these, so each one proves itself by signature or state.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/git")]
[AllowAnonymous]
public class GitCallbackController(
    GitWebhookIntake intake,
    GitAuthState authState,
    GitAccessProvider access,
    GitSyncService sync,
    IRepository<GitConnection, Guid> connections,
    IConfiguration configuration) : ControllerBase
{
    private async Task<string> ReadBodyAsync()
    {
        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync();
    }

    private string? Header(string name) => Request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;

    [HttpPost("webhooks/gitea/{repositoryId:guid}")]
    public async Task<IActionResult> GiteaAsync(Guid repositoryId)
    {
        var accepted = await intake.AcceptGiteaAsync(
            repositoryId, Header("X-Gitea-Delivery"), Header("X-Gitea-Event"), Header("X-Gitea-Signature"), await ReadBodyAsync());
        return accepted ? Ok() : Unauthorized();
    }

    [HttpPost("webhooks/github")]
    public async Task<IActionResult> GitHubAsync()
    {
        var accepted = await intake.AcceptGitHubAsync(
            Header("X-GitHub-Delivery"), Header("X-GitHub-Event"), Header("X-Hub-Signature-256"), await ReadBodyAsync());
        return accepted ? Ok() : Unauthorized();
    }

    [HttpGet("oauth/callback")]
    public async Task<IActionResult> OAuthCallbackAsync(string? code, string? state)
    {
        if (authState.Read(state) is { } id && !string.IsNullOrEmpty(code))
        {
            var connection = await connections.GetAsync(id);
            try
            {
                await access.RedeemCodeAsync(connection, code, authState.OAuthRedirectUri);
                await sync.SyncAsync(connection);
            }
            catch (Exception failure) when (failure is GitAuthException or GitProviderException)
            {
                connection.Status = GitConnectionStatus.NeedsAttention;
                connection.Problem = GitConnectionProblem.TokenRevoked;
                await connections.UpdateAsync(connection);
            }
        }

        return BackToAdmin();
    }

    [HttpGet("github/setup")]
    public async Task<IActionResult> GitHubSetupAsync([FromQuery(Name = "installation_id")] long? installationId, string? state)
    {
        if (authState.Read(state) is { } id && installationId is not null)
        {
            var connection = await connections.GetAsync(id);
            connection.InstallationId = installationId;
            await sync.SyncAsync(connection);
        }

        return BackToAdmin();
    }

    private RedirectResult BackToAdmin() =>
        Redirect($"{(configuration["Auth:SpaBaseUrl"] ?? string.Empty).TrimEnd('/')}/admin/git-connections");
}
