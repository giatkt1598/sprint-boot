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
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Timing;

namespace SprintBoot.GitIntegration;

public static class GitUrls
{
    /// <summary>Splits "https://git.example.com/acme" into the server and the organization.</summary>
    public static (string BaseUrl, string? Owner) Split(string? url)
    {
        var text = url?.Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new BusinessException(GitErrors.InvalidConnection);
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var server = uri.GetLeftPart(UriPartial.Authority);
        return segments.Length == 0
            ? (server, null)
            : ($"{server}/{string.Join('/', segments[..^1])}".TrimEnd('/'), segments[^1]);
    }
}

[Authorize]
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
    IProjectAccess projects,
    IStringEncryptionService encryption,
    IConfiguration configuration)
    : ApplicationService, IGitConnectionAppService
{
    private const string GitHubHost = "github.com";

    public async Task<List<GitConnectionDto>> GetListAsync(Guid projectId)
    {
        await projects.CheckAsync(projectId, ProjectPermissions.Manage);

        var list = await connections.GetListAsync(item => item.ProjectId == projectId);
        var ids = list.Select(item => item.Id).ToList();
        var repos = await repositories.GetListAsync(item => ids.Contains(item.ConnectionId));
        return [.. list.OrderBy(item => item.Name).Select(item => ToDto(item, repos))];
    }

    [Authorize(SprintBootPermissions.ManageGit)]
    public async Task<List<GitConnectionDto>> GetUnassignedAsync()
    {
        var list = await connections.GetListAsync(item => item.ProjectId == null);
        var ids = list.Select(item => item.Id).ToList();
        var repos = await repositories.GetListAsync(item => ids.Contains(item.ConnectionId));
        return [.. list.OrderBy(item => item.Name).Select(item => ToDto(item, repos))];
    }

    [Authorize(SprintBootPermissions.ManageGit)]
    public async Task<GitConnectionDto> AssignAsync(Guid id, AssignGitConnectionInput input)
    {
        await projects.CheckAsync(input.ProjectId, ProjectPermissions.Manage);
        var connection = await connections.GetAsync(id);
        if (connection.ProjectId is not null)
        {
            throw new BusinessException(GitErrors.InvalidConnection);
        }

        connection.AssignTo(input.ProjectId);
        await connections.UpdateAsync(connection);
        return await ToDtoAsync(connection);
    }

    public Task<GitRedirectUrisDto> GetRedirectUrisAsync() => Task.FromResult(new GitRedirectUrisDto
    {
        OAuth = authState.OAuthRedirectUri,
        GitHubSetup = authState.GitHubSetupUri,
    });

    public async Task<GitConnectionDto> GetAsync(Guid id) => await ToDtoAsync(await LoadAsync(id));

    private async Task<GitConnection> LoadAsync(Guid id)
    {
        var connection = await connections.GetAsync(id);
        if (connection.ProjectId is { } projectId)
        {
            await projects.CheckAsync(projectId, ProjectPermissions.Manage);
        }
        else
        {
            await AuthorizationService.CheckAsync(SprintBootPermissions.ManageGit);
        }

        return connection;
    }

    public async Task<GitConnectionDto> CreateAsync(SaveGitConnectionInput input)
    {
        await projects.CheckAsync(input.ProjectId, ProjectPermissions.Manage);
        var (baseUrl, owner) = input.ProviderType == GitProviderType.GitHub ? (GitHubHost, null) : GitUrls.Split(input.BaseUrl);
        var authType = input.ProviderType == GitProviderType.GitHub ? GitAuthType.GitHubApp : input.AuthType;
        var connection = new GitConnection(GuidGenerator.Create(), input.ProjectId, input.Name, input.ProviderType, baseUrl, owner, authType);
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
        var connection = await LoadAsync(id);
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
        await LoadAsync(id);
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
                var connection = await LoadAsync(id);
                type = connection.ProviderType;
                credential = string.IsNullOrEmpty(input.AccessToken)
                    ? await access.GetAsync(connection)
                    : new GitAccess(connection.BaseUrl, input.AccessToken);
            }
            else
            {
                type = GitProviderType.Gitea;
                credential = new GitAccess(GitUrls.Split(input.BaseUrl).BaseUrl, input.AccessToken ?? string.Empty);
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
        var connection = await LoadAsync(id);
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
        await LoadAsync(id);
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
                    IsSelected = item.IsSelected,
                }),
        ];
    }

    public async Task<GitConnectionDto> SetRepositoryAccessAsync(Guid id, SetRepositoryAccessInput input)
    {
        var connection = await LoadAsync(id);
        if (!Enum.IsDefined(input.RepositoryAccess))
        {
            throw new BusinessException(GitErrors.InvalidConnection);
        }

        connection.RepositoryAccess = input.RepositoryAccess;
        await connections.UpdateAsync(connection, autoSave: true);
        return await ToDtoAsync(connection);
    }

    public async Task<GitRepositoryDto> SetRepositorySelectedAsync(Guid repositoryId, SetRepositorySelectedInput input)
    {
        var repository = await repositories.GetAsync(repositoryId);
        await LoadAsync(repository.ConnectionId);
        repository.IsSelected = input.IsSelected;
        await repositories.UpdateAsync(repository, autoSave: true);
        return new GitRepositoryDto
        {
            Id = repository.Id,
            FullName = repository.FullName,
            IsPrivate = repository.IsPrivate,
            DefaultBranch = repository.DefaultBranch,
            WebhookState = repository.WebhookState,
            LastEventAt = repository.LastEventAt,
            IsAccessible = repository.IsAccessible,
            IsSelected = repository.IsSelected,
        };
    }

    public async Task<GitConnectionDto> SyncAsync(Guid id)
    {
        var connection = await LoadAsync(id);
        await sync.SyncAsync(connection);
        return await ToDtoAsync(connection);
    }

    public async Task<GitWebhookSetupDto> GetWebhookSetupAsync(Guid repositoryId)
    {
        var repository = await repositories.GetAsync(repositoryId);
        await LoadAsync(repository.ConnectionId);
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
            BaseUrl = connection.Owner is null ? connection.BaseUrl : $"{connection.BaseUrl}/{connection.Owner}",
            AuthType = connection.AuthType,
            Status = connection.Status,
            Problem = connection.Problem,
            LastSyncAt = connection.LastSyncAt,
            RepositoryCount = own.Count(connection.Uses),
            RepositoryAccess = connection.RepositoryAccess,
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
    GitEventProcessor processor,
    IDistributedEventBus eventBus,
    IConfiguration configuration)
    : ApplicationService, IGitIssueAppService
{
    private const int DefaultPollSeconds = 60;
    private const int DefaultPollRepositories = 10;

    public async Task<DevelopmentDto> GetDevelopmentAsync(Guid issueId)
    {
        await projects.CheckAsync(await issues.GetProjectIdAsync(issueId), ProjectPermissions.ViewIssues);
        var issueLinks = await links.GetListAsync(item => item.IssueId == issueId);
        var ids = issueLinks.Select(item => item.TargetId).ToList();

        var branchRows = await branches.GetListAsync(item => ids.Contains(item.Id));
        var commitRows = await commits.GetListAsync(item => ids.Contains(item.Id));
        var pullRows = await pullRequests.GetListAsync(item => ids.Contains(item.Id));

        var repositoryIds = branchRows.Select(item => item.RepositoryId)
            .Concat(commitRows.Select(item => item.RepositoryId))
            .Concat(pullRows.Select(item => item.RepositoryId))
            .Distinct().ToList();
        var repos = (await repositories.GetListAsync(item => repositoryIds.Contains(item.Id))).ToDictionary(item => item.Id);
        var connectionIds = repos.Values.Select(item => item.ConnectionId).ToList();
        var owners = (await connections.GetListAsync(item => connectionIds.Contains(item.Id))).ToDictionary(item => item.Id);

        var shas = commitRows.Select(item => item.Sha).Concat(pullRows.Select(item => item.HeadSha)).ToList();
        var builds = await statuses.GetListAsync(item => shas.Contains(item.Sha));

        return new DevelopmentDto
        {
            CanCreateBranch = await UsableRepositoriesAsync(await issues.GetProjectIdAsync(issueId)) is { Count: > 0 },
            Branches = [.. branchRows.OrderBy(item => item.Name).Select(item => new GitBranchRowDto
            {
                Name = item.Name,
                RepositoryName = repos[item.RepositoryId].FullName,
                Url = BranchUrl(owners[repos[item.RepositoryId].ConnectionId], repos[item.RepositoryId], item.Name),
            })],
            Commits = [.. commitRows.OrderByDescending(item => item.CommittedAt).Select(item => new GitCommitRowDto
            {
                Sha = item.Sha,
                RepositoryName = repos[item.RepositoryId].FullName,
                Message = item.Message,
                AuthorName = item.AuthorName,
                CommittedAt = item.CommittedAt,
                Url = item.Url,
            })],
            PullRequests = [.. pullRows.OrderByDescending(item => item.Number).Select(item => new GitPullRequestRowDto
            {
                Number = item.Number,
                RepositoryName = repos[item.RepositoryId].FullName,
                Title = item.Title,
                State = item.State,
                Url = item.Url,
            })],
            Builds = [.. builds.OrderByDescending(item => item.UpdatedAt).Select(item => new GitBuildRowDto
            {
                Name = item.Name,
                State = item.State,
                Url = item.Url,
                UpdatedAt = item.UpdatedAt,
            })],
        };
    }

    public async Task<BranchOptionsDto> GetBranchOptionsAsync(Guid issueId)
    {
        var projectId = await issues.GetProjectIdAsync(issueId);
        await projects.CheckAsync(projectId, ProjectPermissions.EditIssues);
        var issue = await facts.FindAsync(issueId) ?? throw new BusinessException(GitErrors.InvalidConnection);

        var used = await UsedRepositoriesAsync(projectId);
        var repos = await UsableRepositoriesAsync(projectId);
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
        var projectId = await issues.GetProjectIdAsync(issueId);
        await projects.CheckAsync(projectId, ProjectPermissions.EditIssues);
        var (repository, connection) = await TargetAsync(repositoryId, projectId);
        return await CallAsync(async () =>
            (await access.Provider(connection.ProviderType).ListBranchesAsync(await access.GetAsync(connection), repository.FullName)).ToList());
    }

    public async Task<GitBranchRowDto> CreateBranchAsync(Guid issueId, CreateBranchInput input)
    {
        var projectId = await issues.GetProjectIdAsync(issueId);
        await projects.CheckAsync(projectId, ProjectPermissions.EditIssues);
        var (repository, connection) = await TargetAsync(input.RepositoryId, projectId);

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

    public async Task RefreshAsync(Guid issueId)
    {
        var projectId = await issues.GetProjectIdAsync(issueId);
        await projects.CheckAsync(projectId, ProjectPermissions.ViewIssues);

        var usable = (await connections.GetListAsync(item => item.ProjectId == projectId && item.Status == GitConnectionStatus.Connected))
            .ToDictionary(item => item.Id);
        var ids = usable.Keys.ToList();
        var repos = (await repositories.GetListAsync(item => item.IsAccessible && ids.Contains(item.ConnectionId)))
            .Where(item => usable[item.ConnectionId].Uses(item))
            .ToList();
        var linked = await RepositoriesOfIssueAsync(issueId);
        var cutoff = Clock.Now.AddSeconds(-configuration.GetValue("Git:Polling:IntervalSeconds", DefaultPollSeconds));
        var due = repos
            .Where(item => item.LastPolledAt is null || item.LastPolledAt < cutoff)
            .OrderByDescending(item => linked.Contains(item.Id))
            .ThenByDescending(item => item.LastEventAt)
            .Take(configuration.GetValue("Git:Polling:MaxRepositories", DefaultPollRepositories))
            .Select(item => item.Id);

        var query = await repositories.GetQueryableAsync();
        var now = Clock.Now;
        foreach (var id in due.ToList())
        {
            // Claimed in the database, so several readers on several replicas poll it once.
            var claimed = await query
                .Where(item => item.Id == id && (item.LastPolledAt == null || item.LastPolledAt < cutoff))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LastPolledAt, now));
            if (claimed > 0)
            {
                await eventBus.PublishAsync(new GitPollRequestedEto { RepositoryId = id }, useOutbox: true);
            }
        }
    }

    private async Task<HashSet<Guid>> RepositoriesOfIssueAsync(Guid issueId)
    {
        var ids = (await links.GetListAsync(item => item.IssueId == issueId)).Select(item => item.TargetId).ToList();
        return
        [
            .. (await branches.GetListAsync(item => ids.Contains(item.Id))).Select(item => item.RepositoryId),
            .. (await commits.GetListAsync(item => ids.Contains(item.Id))).Select(item => item.RepositoryId),
            .. (await pullRequests.GetListAsync(item => ids.Contains(item.Id))).Select(item => item.RepositoryId),
        ];
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

    private async Task<List<GitRepository>> UsableRepositoriesAsync(Guid projectId)
    {
        var own = (await connections.GetListAsync(item => item.ProjectId == projectId)).ToDictionary(item => item.Id);
        var ids = own.Keys.ToList();
        return [.. (await repositories.GetListAsync(item => item.IsAccessible && ids.Contains(item.ConnectionId)))
            .Where(item => own[item.ConnectionId].Uses(item))];
    }

    private async Task<(GitRepository Repository, GitConnection Connection)> TargetAsync(Guid repositoryId, Guid projectId)
    {
        var repository = await repositories.GetAsync(repositoryId);
        var connection = await connections.GetAsync(repository.ConnectionId);
        return connection.ProjectId == projectId && connection.Uses(repository)
            ? (repository, connection)
            : throw new BusinessException(GitErrors.InvalidConnection);
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

public class ProjectGitFacts(IRepository<IssueGitLink, Guid> links, IRepository<GitPullRequest, Guid> pullRequests)
    : IProjectGitFacts, ITransientDependency
{
    public async Task<PullRequestCounts> GetPullRequestCountsAsync(Guid projectId)
    {
        var linked = (await links.GetQueryableAsync())
            .Where(link => link.ProjectId == projectId && link.TargetType == GitLinkTarget.PullRequest)
            .Select(link => link.TargetId);
        var states = await (await pullRequests.GetQueryableAsync())
            .Where(pull => linked.Contains(pull.Id))
            .GroupBy(pull => pull.State)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync();
        int Count(GitPullRequestState state) => states.FirstOrDefault(item => item.Key == state)?.Count ?? 0;
        return new PullRequestCounts(states.Sum(item => item.Count), Count(GitPullRequestState.Open), Count(GitPullRequestState.Merged));
    }
}

/// <summary>Signed, short-lived state so a provider redirect can only finish a setup an admin started.</summary>
public class GitAuthState(IDataProtectionProvider protection, GitSyncService sync) : ITransientDependency
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private ITimeLimitedDataProtector Protector => protection.CreateProtector("SprintBoot.Git.Setup").ToTimeLimitedDataProtector();

    public string OAuthRedirectUri => $"{sync.PublicUrl}/api/v1/git/oauth/callback";

    public string GitHubSetupUri => $"{sync.PublicUrl}/api/v1/git/github/setup";

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
    IProjectAccess projects,
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

            return await BackToSettingsAsync(connection);
        }

        return BackToSettings(null);
    }

    [HttpGet("github/setup")]
    public async Task<IActionResult> GitHubSetupAsync([FromQuery(Name = "installation_id")] long? installationId, string? state)
    {
        if (authState.Read(state) is { } id && installationId is not null)
        {
            var connection = await connections.GetAsync(id);
            connection.InstallationId = installationId;
            await sync.SyncAsync(connection);
            return await BackToSettingsAsync(connection);
        }

        return BackToSettings(null);
    }

    private async Task<RedirectResult> BackToSettingsAsync(GitConnection connection) =>
        BackToSettings(connection.ProjectId is { } projectId ? (await projects.GetRefAsync(projectId)).Key : null);

    private RedirectResult BackToSettings(string? spaceKey) =>
        Redirect($"{(configuration["Auth:SpaBaseUrl"] ?? string.Empty).TrimEnd('/')}/{(spaceKey is null ? string.Empty : $"spaces/{spaceKey}/settings/git")}");
}
