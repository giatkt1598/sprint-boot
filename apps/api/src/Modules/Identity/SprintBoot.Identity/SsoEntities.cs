using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.Identity;

public class ExternalProviderConfig : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public string DisplayName { get; set; } = null!;

    public ExternalProviderType Type { get; set; }

    public string? DiscoveryUrl { get; set; }

    public string ClientId { get; set; } = null!;

    public string? EncryptedSecret { get; set; }

    public string? Scopes { get; set; }

    public string? EmailClaim { get; set; }

    public string? NameClaim { get; set; }

    public bool IsEnabled { get; set; }

    protected ExternalProviderConfig()
    {
    }

    public ExternalProviderConfig(Guid id, string name)
        : base(id)
    {
        Name = name;
    }
}

public class AuthPolicy : Entity<Guid>
{
    public bool LocalLoginEnabled { get; set; } = true;

    public bool SelfRegistrationEnabled { get; set; }

    public string? AllowedDomains { get; set; }

    protected AuthPolicy()
    {
    }

    public AuthPolicy(Guid id)
        : base(id)
    {
    }
}

public class Invitation : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Email { get; private set; } = null!;

    public string TokenHash { get; set; } = null!;

    public InvitationStatus Status { get; set; }

    public string? SystemRole { get; set; }

    public Guid InvitedById { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public InvitationEmailStatus EmailStatus { get; set; }

    protected Invitation()
    {
    }

    public Invitation(Guid id, string email, string tokenHash, string? systemRole, Guid invitedById, DateTime expiresAt)
        : base(id)
    {
        Email = email;
        TokenHash = tokenHash;
        SystemRole = systemRole;
        InvitedById = invitedById;
        ExpiresAt = expiresAt;
    }

    public bool IsUsable(DateTime now) => Status == InvitationStatus.Pending && ExpiresAt > now;

    public InvitationStatus EffectiveStatus(DateTime now) =>
        Status == InvitationStatus.Pending && ExpiresAt <= now ? InvitationStatus.Expired : Status;
}

public class InvitationProject : Entity<Guid>
{
    public Guid InvitationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string ProjectRole { get; private set; } = null!;

    protected InvitationProject()
    {
    }

    public InvitationProject(Guid id, Guid invitationId, Guid projectId, string projectRole)
        : base(id)
    {
        InvitationId = invitationId;
        ProjectId = projectId;
        ProjectRole = projectRole;
    }
}
