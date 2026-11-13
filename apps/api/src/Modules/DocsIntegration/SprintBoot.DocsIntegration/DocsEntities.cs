using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.DocsIntegration;

/// <summary>The one Docmost a workspace talks to, both ways.</summary>
public class DocsConnection : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string BaseUrl { get; set; } = null!;

    /// <summary>The Docmost API key of a service user, encrypted.</summary>
    public string? EncryptedApiKey { get; set; }

    /// <summary>SHA-256 of the secret Docmost presents on its calls; the secret itself is shown once.</summary>
    public string? BridgeSecretHash { get; set; }

    public DocsConnectionStatus Status { get; set; }

    public DocsProblem Problem { get; set; }

    /// <summary>Docmost's own words on the last failed check.</summary>
    public string? ProblemDetail { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public string? DocmostUser { get; set; }

    protected DocsConnection()
    {
    }

    public DocsConnection(Guid id, string baseUrl)
        : base(id)
    {
        BaseUrl = baseUrl;
    }
}

/// <summary>The Docmost space a Sprint Boot space keeps its documents in.</summary>
public class ProjectDocsSpace : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public string DocmostSpaceId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    protected ProjectDocsSpace()
    {
    }

    public ProjectDocsSpace(Guid id, Guid projectId, string docmostSpaceId, string name, string slug)
        : base(id)
    {
        ProjectId = projectId;
        DocmostSpaceId = docmostSpaceId;
        Name = name;
        Slug = slug;
    }
}

/// <summary>A Docmost page that belongs with a ticket, kept up to date by Docmost's saves.</summary>
public class TicketDocLink : Entity<Guid>
{
    public Guid TicketId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string PageId { get; private set; } = null!;

    public DocLinkSource Source { get; private set; }

    public string Title { get; set; } = null!;

    public string Url { get; set; } = null!;

    public string? SpaceName { get; set; }

    public DateTime UpdatedAt { get; set; }

    protected TicketDocLink()
    {
    }

    public TicketDocLink(Guid id, Guid ticketId, Guid projectId, string pageId, DocLinkSource source, string title, string url, string? spaceName, DateTime updatedAt)
        : base(id)
    {
        TicketId = ticketId;
        ProjectId = projectId;
        PageId = pageId;
        Source = source;
        Title = title;
        Url = url;
        SpaceName = spaceName;
        UpdatedAt = updatedAt;
    }
}

public static class DocsIntegrationModelBuilderExtensions
{
    public const string Schema = "docs";

    public static void ConfigureDocsIntegration(this ModelBuilder builder)
    {
        builder.Entity<DocsConnection>(b =>
        {
            b.ToTable("connections", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.BaseUrl).HasMaxLength(512).IsRequired();
            b.Property(item => item.BridgeSecretHash).HasMaxLength(64);
            b.Property(item => item.DocmostUser).HasMaxLength(256);
            b.Property(item => item.ProblemDetail).HasMaxLength(512);
        });

        builder.Entity<ProjectDocsSpace>(b =>
        {
            b.ToTable("project_spaces", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.DocmostSpaceId).HasMaxLength(64).IsRequired();
            b.Property(item => item.Name).HasMaxLength(256).IsRequired();
            b.Property(item => item.Slug).HasMaxLength(256).IsRequired();
            b.HasIndex(item => item.ProjectId).IsUnique();
            b.HasIndex(item => item.DocmostSpaceId);
        });

        builder.Entity<TicketDocLink>(b =>
        {
            b.ToTable("ticket_links", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.PageId).HasMaxLength(64).IsRequired();
            b.Property(item => item.Title).HasMaxLength(512).IsRequired();
            b.Property(item => item.Url).HasMaxLength(2048).IsRequired();
            b.Property(item => item.SpaceName).HasMaxLength(256);
            b.HasIndex(item => new { item.TicketId, item.PageId, item.Source }).IsUnique();
            b.HasIndex(item => item.PageId);
            b.HasIndex(item => item.ProjectId);
        });
    }
}
