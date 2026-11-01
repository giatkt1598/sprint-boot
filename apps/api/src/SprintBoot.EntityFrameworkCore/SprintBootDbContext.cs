using Microsoft.EntityFrameworkCore;
using SprintBoot.Agile;
using SprintBoot.Issues;
using SprintBoot.Platform;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;

namespace SprintBoot.EntityFrameworkCore;

/// <summary>
/// Unified DbContext: all modules share one connection and transaction so the outbox
/// commits together with business data. All migrations live here.
/// </summary>
[ConnectionStringName("Default")]
[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(IPlatformDbContext))]
public class SprintBootDbContext(DbContextOptions<SprintBootDbContext> options)
    : AbpDbContext<SprintBootDbContext>(options),
        IIdentityDbContext,
        IPlatformDbContext,
        IHasEventOutbox,
        IHasEventInbox
{
    public DbSet<IdentityUser> Users { get; set; } = null!;
    public DbSet<IdentityRole> Roles { get; set; } = null!;
    public DbSet<IdentityClaimType> ClaimTypes { get; set; } = null!;
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; } = null!;
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; } = null!;
    public DbSet<IdentityLinkUser> LinkUsers { get; set; } = null!;
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; } = null!;
    public DbSet<IdentitySession> Sessions { get; set; } = null!;

    public DbSet<SystemPingReceipt> SystemPingReceipts { get; set; } = null!;

    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<ProjectMember> ProjectMembers { get; set; } = null!;
    public DbSet<ProjectRole> ProjectRoles { get; set; } = null!;
    public DbSet<ProjectRolePermission> ProjectRolePermissions { get; set; } = null!;

    public DbSet<Status> Statuses { get; set; } = null!;
    public DbSet<Workflow> Workflows { get; set; } = null!;
    public DbSet<WorkflowVersion> WorkflowVersions { get; set; } = null!;
    public DbSet<WorkflowStep> WorkflowSteps { get; set; } = null!;
    public DbSet<Transition> Transitions { get; set; } = null!;
    public DbSet<TransitionRule> TransitionRules { get; set; } = null!;
    public DbSet<ProjectWorkflowMapping> ProjectWorkflowMappings { get; set; } = null!;
    public DbSet<WorkflowMigration> WorkflowMigrations { get; set; } = null!;
    public DbSet<WebhookDelivery> WebhookDeliveries { get; set; } = null!;

    public DbSet<IssueType> IssueTypes { get; set; } = null!;
    public DbSet<Priority> Priorities { get; set; } = null!;
    public DbSet<Issue> Issues { get; set; } = null!;
    public DbSet<Label> Labels { get; set; } = null!;
    public DbSet<IssueLabel> IssueLabels { get; set; } = null!;
    public DbSet<Comment> Comments { get; set; } = null!;
    public DbSet<CommentReaction> CommentReactions { get; set; } = null!;
    public DbSet<Attachment> Attachments { get; set; } = null!;
    public DbSet<IssueActivity> IssueActivities { get; set; } = null!;
    public DbSet<WorkLog> WorkLogs { get; set; } = null!;

    public DbSet<Board> Boards { get; set; } = null!;
    public DbSet<BoardColumn> BoardColumns { get; set; } = null!;
    public DbSet<BoardColumnStatus> BoardColumnStatuses { get; set; } = null!;
    public DbSet<BoardEntry> BoardEntries { get; set; } = null!;
    public DbSet<Sprint> Sprints { get; set; } = null!;
    public DbSet<SprintIssue> SprintIssues { get; set; } = null!;

    public DbSet<OutgoingEventRecord> OutgoingEvents { get; set; } = null!;
    public DbSet<IncomingEventRecord> IncomingEvents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureAuditLogging();
        builder.ConfigureEventOutbox();
        builder.ConfigureEventInbox();
        builder.ConfigurePlatform();
        builder.ConfigureProjects();
        builder.ConfigureWorkflows();
        builder.ConfigureIssues();
        builder.ConfigureAgile();
    }
}
