using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.GitIntegration;

public class GitConnection : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; set; } = null!;

    public GitProviderType ProviderType { get; private set; }

    /// <summary>Null only on connections made before they belonged to a space.</summary>
    public Guid? ProjectId { get; private set; }

    public string BaseUrl { get; private set; } = null!;

    /// <summary>When set, only this organization or user is ever touched.</summary>
    public string? Owner { get; private set; }

    public GitAuthType AuthType { get; set; }

    public string? EncryptedCredentials { get; set; }

    public long? InstallationId { get; set; }

    public GitConnectionStatus Status { get; set; }

    public GitConnectionProblem Problem { get; set; }

    public DateTime? LastSyncAt { get; set; }

    protected GitConnection()
    {
    }

    public GitConnection(Guid id, Guid projectId, string name, GitProviderType providerType, string baseUrl, string? owner, GitAuthType authType)
        : base(id)
    {
        ProjectId = projectId;
        Name = name;
        ProviderType = providerType;
        BaseUrl = baseUrl;
        Owner = owner;
        AuthType = authType;
    }

    public bool Covers(string fullName) =>
        Owner is null || fullName.StartsWith($"{Owner}/", StringComparison.OrdinalIgnoreCase);
}

public class GitRepository : Entity<Guid>
{
    public Guid ConnectionId { get; private set; }

    public string ExternalId { get; private set; } = null!;

    public string FullName { get; set; } = null!;

    public string DefaultBranch { get; set; } = null!;

    public string WebUrl { get; set; } = null!;

    public bool IsPrivate { get; set; }

    public bool IsAccessible { get; set; } = true;

    public string? EncryptedWebhookSecret { get; set; }

    public string? ExternalHookId { get; set; }

    public GitWebhookState WebhookState { get; set; }

    public DateTime? LastEventAt { get; set; }

    protected GitRepository()
    {
    }

    public GitRepository(Guid id, Guid connectionId, string externalId)
        : base(id)
    {
        ConnectionId = connectionId;
        ExternalId = externalId;
    }
}

public class GitBranch : Entity<Guid>
{
    public Guid RepositoryId { get; private set; }

    public string Name { get; private set; } = null!;

    protected GitBranch()
    {
    }

    public GitBranch(Guid id, Guid repositoryId, string name)
        : base(id)
    {
        RepositoryId = repositoryId;
        Name = name;
    }
}

public class GitCommit : Entity<Guid>
{
    public Guid RepositoryId { get; private set; }

    public string Sha { get; private set; } = null!;

    public string Message { get; private set; } = null!;

    public string? AuthorName { get; private set; }

    public DateTime CommittedAt { get; private set; }

    public string Url { get; private set; } = null!;

    protected GitCommit()
    {
    }

    public GitCommit(Guid id, Guid repositoryId, string sha, string message, string? authorName, DateTime committedAt, string url)
        : base(id)
    {
        RepositoryId = repositoryId;
        Sha = sha;
        Message = message;
        AuthorName = authorName;
        CommittedAt = committedAt;
        Url = url;
    }
}

public class GitPullRequest : Entity<Guid>
{
    public Guid RepositoryId { get; private set; }

    public int Number { get; private set; }

    public string Title { get; set; } = null!;

    public GitPullRequestState State { get; set; }

    public string Url { get; set; } = null!;

    public string HeadSha { get; set; } = null!;

    protected GitPullRequest()
    {
    }

    public GitPullRequest(Guid id, Guid repositoryId, int number)
        : base(id)
    {
        RepositoryId = repositoryId;
        Number = number;
    }
}

/// <summary>Keeps the project so branch pickers can favour repositories already used in a space.</summary>
public class IssueGitLink : Entity<Guid>
{
    public Guid IssueId { get; private set; }

    public Guid ProjectId { get; private set; }

    public GitLinkTarget TargetType { get; private set; }

    public Guid TargetId { get; private set; }

    protected IssueGitLink()
    {
    }

    public IssueGitLink(Guid id, Guid issueId, Guid projectId, GitLinkTarget targetType, Guid targetId)
        : base(id)
    {
        IssueId = issueId;
        ProjectId = projectId;
        TargetType = targetType;
        TargetId = targetId;
    }
}

public class CiStatus : Entity<Guid>
{
    public Guid RepositoryId { get; private set; }

    public string Sha { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public GitCiState State { get; set; }

    public string? Url { get; set; }

    public DateTime UpdatedAt { get; set; }

    protected CiStatus()
    {
    }

    public CiStatus(Guid id, Guid repositoryId, string sha, string name)
        : base(id)
    {
        RepositoryId = repositoryId;
        Sha = sha;
        Name = name;
    }
}

public class GitWebhookDelivery : Entity<Guid>
{
    public string DeliveryId { get; private set; } = null!;

    public GitProviderType Provider { get; private set; }

    public Guid? ConnectionId { get; private set; }

    public string EventName { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTime ReceivedAt { get; private set; }

    public DateTime? ProcessedAt { get; set; }

    protected GitWebhookDelivery()
    {
    }

    public GitWebhookDelivery(Guid id, string deliveryId, GitProviderType provider, Guid? connectionId, string eventName, string payload, DateTime receivedAt)
        : base(id)
    {
        DeliveryId = deliveryId;
        Provider = provider;
        ConnectionId = connectionId;
        EventName = eventName;
        Payload = payload;
        ReceivedAt = receivedAt;
    }
}

public static class GitModelBuilderExtensions
{
    public const string Schema = "git";

    public static void ConfigureGitIntegration(this ModelBuilder builder)
    {
        builder.Entity<GitConnection>(b =>
        {
            b.ToTable("connections", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Name).HasMaxLength(128).IsRequired();
            b.Property(item => item.BaseUrl).HasMaxLength(256).IsRequired();
            b.Property(item => item.Owner).HasMaxLength(128);
            b.HasIndex(item => item.ProjectId);
        });
        builder.Entity<GitRepository>(b =>
        {
            b.ToTable("repositories", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.ExternalId).HasMaxLength(64).IsRequired();
            b.Property(item => item.FullName).HasMaxLength(256).IsRequired();
            b.Property(item => item.DefaultBranch).HasMaxLength(255).IsRequired();
            b.Property(item => item.WebUrl).HasMaxLength(512).IsRequired();
            b.Property(item => item.ExternalHookId).HasMaxLength(64);
            b.HasIndex(item => new { item.ConnectionId, item.ExternalId }).IsUnique();
        });
        builder.Entity<GitBranch>(b =>
        {
            b.ToTable("branches", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Name).HasMaxLength(255).IsRequired();
            b.HasIndex(item => new { item.RepositoryId, item.Name }).IsUnique();
        });
        builder.Entity<GitCommit>(b =>
        {
            b.ToTable("commits", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Sha).HasMaxLength(64).IsRequired();
            b.Property(item => item.AuthorName).HasMaxLength(256);
            b.Property(item => item.Url).HasMaxLength(512).IsRequired();
            b.HasIndex(item => new { item.RepositoryId, item.Sha }).IsUnique();
        });
        builder.Entity<GitPullRequest>(b =>
        {
            b.ToTable("pull_requests", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Title).HasMaxLength(512).IsRequired();
            b.Property(item => item.Url).HasMaxLength(512).IsRequired();
            b.Property(item => item.HeadSha).HasMaxLength(64).IsRequired();
            b.HasIndex(item => new { item.RepositoryId, item.Number }).IsUnique();
        });
        builder.Entity<IssueGitLink>(b =>
        {
            b.ToTable("issue_links", Schema);
            b.ConfigureByConvention();
            b.HasIndex(item => new { item.IssueId, item.TargetType, item.TargetId }).IsUnique();
            b.HasIndex(item => item.ProjectId);
        });
        builder.Entity<CiStatus>(b =>
        {
            b.ToTable("ci_statuses", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Sha).HasMaxLength(64).IsRequired();
            b.Property(item => item.Name).HasMaxLength(256).IsRequired();
            b.Property(item => item.Url).HasMaxLength(512);
            b.HasIndex(item => new { item.RepositoryId, item.Sha, item.Name }).IsUnique();
        });
        builder.Entity<GitWebhookDelivery>(b =>
        {
            b.ToTable("webhook_deliveries", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.DeliveryId).HasMaxLength(64).IsRequired();
            b.Property(item => item.EventName).HasMaxLength(64).IsRequired();
            b.HasIndex(item => new { item.Provider, item.DeliveryId }).IsUnique();
        });
    }
}
