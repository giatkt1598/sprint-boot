using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Services;
using Volo.Abp.EventBus;

namespace SprintBoot.GitIntegration;

public enum GitProviderType
{
    GitHub = 0,
    Gitea = 1,
}

public enum GitAuthType
{
    GitHubApp = 0,
    AccessToken = 1,
    OAuth2 = 2,
}

public enum GitConnectionStatus
{
    Pending = 0,
    Connected = 1,
    NeedsAttention = 2,
}

public enum GitConnectionProblem
{
    None = 0,
    TokenRevoked = 1,
    Unreachable = 2,
}

public enum GitWebhookState
{
    NotRegistered = 0,
    Active = 1,
    Failed = 2,
}

public enum GitPullRequestState
{
    Open = 0,
    Merged = 1,
    Closed = 2,
}

public enum GitCiState
{
    Pending = 0,
    Running = 1,
    Success = 2,
    Failure = 3,
}

public enum GitLinkTarget
{
    Branch = 0,
    Commit = 1,
    PullRequest = 2,
}

public class GitConnectionDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required GitProviderType ProviderType { get; set; }

    /// <summary>The server address, with the organization when the connection is limited to one.</summary>
    public required string BaseUrl { get; set; }

    public required GitAuthType AuthType { get; set; }

    public required GitConnectionStatus Status { get; set; }

    public required GitConnectionProblem Problem { get; set; }

    public required DateTime? LastSyncAt { get; set; }

    public required int RepositoryCount { get; set; }

    public required int WebhookProblemCount { get; set; }
}

public class SaveGitConnectionInput
{
    /// <summary>The space the connection belongs to. Ignored when updating.</summary>
    public Guid ProjectId { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; set; }

    public GitProviderType ProviderType { get; set; }

    [StringLength(256)]
    public string? BaseUrl { get; set; }

    public GitAuthType AuthType { get; set; }

    /// <summary>Blank keeps the stored value.</summary>
    public string? AccessToken { get; set; }

    [StringLength(256)]
    public string? ClientId { get; set; }

    /// <summary>Blank keeps the stored value.</summary>
    public string? ClientSecret { get; set; }
}

public class TestGitConnectionInput
{
    public Guid? ConnectionId { get; set; }

    [StringLength(256)]
    public string? BaseUrl { get; set; }

    public string? AccessToken { get; set; }
}

public class AssignGitConnectionInput
{
    public Guid ProjectId { get; set; }
}

public class GitRedirectUrisDto
{
    public required string OAuth { get; set; }

    public required string GitHubSetup { get; set; }
}

public class GitTestResultDto
{
    public required bool Succeeded { get; set; }
}

public class GitAuthorizeUrlDto
{
    public required string Url { get; set; }
}

public class GitRepositoryDto
{
    public required Guid Id { get; set; }

    public required string FullName { get; set; }

    public required bool IsPrivate { get; set; }

    public required string DefaultBranch { get; set; }

    public required GitWebhookState WebhookState { get; set; }

    public required DateTime? LastEventAt { get; set; }

    public required bool IsAccessible { get; set; }
}

public class GitWebhookSetupDto
{
    public required string Url { get; set; }

    public required string Secret { get; set; }
}

public interface IGitConnectionAppService : IApplicationService
{
    Task<List<GitConnectionDto>> GetListAsync(Guid projectId);

    Task<GitRedirectUrisDto> GetRedirectUrisAsync();

    /// <summary>Connections from before they belonged to a space, for an administrator to place.</summary>
    Task<List<GitConnectionDto>> GetUnassignedAsync();

    Task<GitConnectionDto> AssignAsync(Guid id, AssignGitConnectionInput input);

    Task<GitConnectionDto> GetAsync(Guid id);

    Task<GitConnectionDto> CreateAsync(SaveGitConnectionInput input);

    Task<GitConnectionDto> UpdateAsync(Guid id, SaveGitConnectionInput input);

    Task DeleteAsync(Guid id);

    Task<GitTestResultDto> TestAsync(TestGitConnectionInput input);

    Task<GitAuthorizeUrlDto> GetAuthorizeUrlAsync(Guid id);

    Task<List<GitRepositoryDto>> GetRepositoriesAsync(Guid id, string? search);

    Task<GitConnectionDto> SyncAsync(Guid id);

    Task<GitWebhookSetupDto> GetWebhookSetupAsync(Guid repositoryId);
}

public class GitBranchRowDto
{
    public required string Name { get; set; }

    public required string RepositoryName { get; set; }

    public required string Url { get; set; }
}

public class GitCommitRowDto
{
    public required string Sha { get; set; }

    public required string Message { get; set; }

    public required string? AuthorName { get; set; }

    public required DateTime CommittedAt { get; set; }

    public required string Url { get; set; }
}

public class GitPullRequestRowDto
{
    public required int Number { get; set; }

    public required string Title { get; set; }

    public required GitPullRequestState State { get; set; }

    public required string Url { get; set; }
}

public class GitBuildRowDto
{
    public required string Name { get; set; }

    public required GitCiState State { get; set; }

    public required string? Url { get; set; }
}

public class DevelopmentDto
{
    public required bool CanCreateBranch { get; set; }

    public required List<GitBranchRowDto> Branches { get; set; }

    public required List<GitCommitRowDto> Commits { get; set; }

    public required List<GitPullRequestRowDto> PullRequests { get; set; }

    public required GitBuildRowDto? LatestBuild { get; set; }
}

public class BranchRepositoryDto
{
    public required Guid Id { get; set; }

    public required string FullName { get; set; }

    public required string ConnectionName { get; set; }

    public required string DefaultBranch { get; set; }

    public required bool UsedInSpace { get; set; }
}

public class BranchOptionsDto
{
    public required string SuggestedName { get; set; }

    public required List<BranchRepositoryDto> Repositories { get; set; }
}

public class CreateBranchInput
{
    public Guid RepositoryId { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string BaseBranch { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Name { get; set; }
}

public interface IGitIssueAppService : IApplicationService
{
    /// <summary>Reads stored data only, so a provider outage never reaches the issue page.</summary>
    Task<DevelopmentDto> GetDevelopmentAsync(Guid issueId);

    Task<BranchOptionsDto> GetBranchOptionsAsync(Guid issueId);

    Task<List<string>> GetBranchesAsync(Guid issueId, Guid repositoryId);

    Task<GitBranchRowDto> CreateBranchAsync(Guid issueId, CreateBranchInput input);
}

/// <summary>Lets workflow rules ask about development work without touching Git tables.</summary>
public interface IIssueGitFacts
{
    Task<bool> HasPullRequestAsync(Guid issueId);
}

[EventName("SprintBoot.Git.WebhookReceived")]
public class GitWebhookReceivedEto
{
    public Guid DeliveryId { get; set; }
}

public static class GitErrors
{
    public const string BranchExists = "SprintBoot:GitBranchExists";
    public const string ProviderUnavailable = "SprintBoot:GitProviderUnavailable";
    public const string GitHubNotConfigured = "SprintBoot:GitHubAppNotConfigured";
    public const string InvalidConnection = "SprintBoot:GitConnectionInvalid";
    public const string NoCredentials = "SprintBoot:GitCredentialsRequired";
    public const string LinkedPullRequestRequired = "SprintBoot:TransitionLinkedPullRequest";
}
