using System.ComponentModel.DataAnnotations;
using SprintBoot.Identity;
using Volo.Abp.Application.Services;

namespace SprintBoot.Projects;

public enum BoardType
{
    Scrum = 0,
    Kanban = 1,
}

public class ProjectDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Name { get; set; }

    public required BoardType Type { get; set; }

    public required string? BranchNameTemplate { get; set; }

    public required UserRefDto? Lead { get; set; }

    public required int IssueCount { get; set; }

    public required DateTime UpdatedAt { get; set; }

    /// <summary>Keys the caller holds in this project, so the UI can hide actions.</summary>
    public required string[] Permissions { get; set; }
}

public class CreateProjectInput
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; set; }

    [Required]
    [RegularExpression("^[A-Z][A-Z0-9]{1,9}$")]
    public required string Key { get; set; }

    public BoardType Type { get; set; }

    public Guid? LeadId { get; set; }
}

public class UpdateProjectInput
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; set; }

    public Guid? LeadId { get; set; }

    [StringLength(128)]
    public string? BranchNameTemplate { get; set; }
}

public class ProjectMemberDto
{
    public required UserRefDto User { get; set; }

    public required string Role { get; set; }

    public required DateTime JoinedAt { get; set; }
}

public class AddMemberInput
{
    [Required]
    public required Guid UserId { get; set; }

    [Required]
    public required string Role { get; set; }
}

public class UpdateMemberInput
{
    [Required]
    public required string Role { get; set; }
}

public class UserSpaceDto
{
    public required Guid ProjectId { get; set; }

    public required string Key { get; set; }

    public required string Name { get; set; }

    public required string Role { get; set; }

    public required DateTime AddedAt { get; set; }
}

/// <summary>System administration view of a user's memberships.</summary>
public interface IProjectAdminAppService : IApplicationService
{
    Task<List<UserSpaceDto>> GetUserSpacesAsync(Guid userId);
}

public interface IProjectAppService : IApplicationService
{
    Task<List<ProjectDto>> GetListAsync();

    Task<ProjectDto> GetAsync(Guid id);

    Task<ProjectDto> GetByKeyAsync(string key);

    Task<ProjectDto> CreateAsync(CreateProjectInput input);

    Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectInput input);

    Task<List<ProjectMemberDto>> GetMembersAsync(Guid id);

    Task<ProjectMemberDto> AddMemberAsync(Guid id, AddMemberInput input);

    Task<ProjectMemberDto> UpdateMemberAsync(Guid id, Guid userId, UpdateMemberInput input);

    Task RemoveMemberAsync(Guid id, Guid userId);
}

/// <summary>Used by other modules. Every project-scoped call must pass through this.</summary>
public interface IProjectAccess
{
    Task<bool> IsGrantedAsync(Guid projectId, string permission);

    /// <summary>Throws 404 when the caller cannot view the project, 403 when viewing is allowed but the action is not.</summary>
    Task CheckAsync(Guid projectId, string permission);

    /// <summary>Null means every project (system admin).</summary>
    Task<List<Guid>?> GetVisibleProjectIdsAsync();

    Task<ProjectRef> GetRefAsync(Guid projectId);

    Task<string?> GetBranchTemplateAsync(Guid projectId);

    Task<ProjectRef?> FindRefByKeyAsync(string key);

    Task<List<ProjectRef>> GetAllRefsAsync();

    Task<Dictionary<Guid, ProjectRef>> GetRefsAsync(IEnumerable<Guid> projectIds);

    Task<bool> IsMemberAsync(Guid projectId, Guid userId);

    /// <summary>Members with their role, for background work that has no calling user.</summary>
    Task<Dictionary<Guid, string>> GetMemberRolesAsync(Guid projectId);

    /// <summary>The caller's role name in the project, null for outsiders.</summary>
    Task<string?> GetCurrentRoleAsync(Guid projectId);

    /// <summary>Takes the next issue number atomically; numbers are never reused.</summary>
    Task<int> NextIssueNumberAsync(Guid projectId);
}

/// <summary>Adds someone without a calling Project Admin, for invitations already authorized.</summary>
public interface IProjectMembership
{
    /// <summary>Does nothing when the project is gone or the user already belongs to it.</summary>
    Task AddAsync(Guid projectId, Guid userId, string role, Guid actorId);
}

public record ProjectRef(Guid Id, string Key, string Name, BoardType Type);

/// <summary>Raised in-process inside the creating transaction so modules can add defaults.</summary>
public record ProjectCreatedEvent(Guid ProjectId);

[Volo.Abp.EventBus.EventName("SprintBoot.Projects.MemberAdded")]
public class ProjectMemberAddedEto
{
    public Guid EventId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public Guid ActorId { get; set; }
}

public static class ProjectErrors
{
    public const string KeyTaken = "SprintBoot:ProjectKeyTaken";
    public const string InvalidRole = "SprintBoot:InvalidProjectRole";
    public const string AlreadyMember = "SprintBoot:AlreadyMember";
    public const string LastAdmin = "SprintBoot:LastProjectAdmin";
}
