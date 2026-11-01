using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.Projects;

public class Project : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Key { get; private set; } = null!;

    public string Name { get; set; } = null!;

    public Guid? LeadId { get; set; }

    public BoardType DefaultBoardType { get; private set; }

    public int NextIssueNumber { get; private set; } = 1;

    public string? BranchNameTemplate { get; set; }

    public int SprintLengthDays { get; set; } = 14;

    protected Project()
    {
    }

    public Project(Guid id, string key, string name, BoardType boardType, Guid? leadId)
        : base(id)
    {
        Key = key;
        Name = name;
        DefaultBoardType = boardType;
        LeadId = leadId;
    }
}

public class ProjectMember : CreationAuditedEntity<Guid>
{
    public Guid ProjectId { get; private set; }

    public Guid UserId { get; private set; }

    public string Role { get; set; } = null!;

    protected ProjectMember()
    {
    }

    public ProjectMember(Guid id, Guid projectId, Guid userId, string role)
        : base(id)
    {
        ProjectId = projectId;
        UserId = userId;
        Role = role;
    }
}

public class ProjectRole : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = null!;

    protected ProjectRole()
    {
    }

    public ProjectRole(Guid id, Guid projectId, string name)
        : base(id)
    {
        ProjectId = projectId;
        Name = name;
    }
}

public class ProjectRolePermission : Entity<Guid>
{
    public Guid RoleId { get; private set; }

    public string PermissionKey { get; private set; } = null!;

    protected ProjectRolePermission()
    {
    }

    public ProjectRolePermission(Guid id, Guid roleId, string permissionKey)
        : base(id)
    {
        RoleId = roleId;
        PermissionKey = permissionKey;
    }
}
