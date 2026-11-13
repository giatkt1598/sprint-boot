using Volo.Abp.Content;
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

    public required int SprintLengthDays { get; set; }

    public required UserRefDto? Lead { get; set; }

    public required int TicketCount { get; set; }

    public required DateTime UpdatedAt { get; set; }

    /// <summary>Keys the caller holds in this project, so the UI can hide actions.</summary>
    public required string[] Permissions { get; set; }

    /// <summary>Null until an admin uploads one; clients then show the key.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Set while the space is archived and read-only; its permissions then hold viewing only.</summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>The caller may archive the space or bring it back; true for those who manage it.</summary>
    public bool CanArchive { get; set; }
}

/// <summary>A deleted space waiting in the trash until it is purged.</summary>
public class TrashedProjectDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Name { get; set; }

    public required DateTime DeletedAt { get; set; }

    public required UserRefDto? DeletedBy { get; set; }

    /// <summary>When the background job removes it for good; until then it can be restored.</summary>
    public required DateTime PurgeAt { get; set; }

    public string? LogoUrl { get; set; }
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

    /// <summary>Blank keeps the current length.</summary>
    [Range(1, 365)]
    public int? SprintLengthDays { get; set; }
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

    public string? LogoUrl { get; set; }
}

/// <summary>System administration view of a user's memberships.</summary>
public interface IProjectAdminAppService : IApplicationService
{
    Task<List<UserSpaceDto>> GetUserSpacesAsync(Guid userId);
}

public interface IProjectAppService : IApplicationService
{
    /// <summary>Archived spaces are left out unless asked for.</summary>
    Task<List<ProjectDto>> GetListAsync(bool includeArchived = false);

    /// <summary>The spaces the caller is a member of, with their role in each.</summary>
    Task<List<UserSpaceDto>> GetMySpacesAsync();

    Task<ProjectDto> GetAsync(Guid id);

    Task<ProjectDto> GetByKeyAsync(string key);

    Task<ProjectDto> CreateAsync(CreateProjectInput input);

    Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectInput input);

    Task<List<ProjectMemberDto>> GetMembersAsync(Guid id);

    Task<ProjectMemberDto> AddMemberAsync(Guid id, AddMemberInput input);

    Task<ProjectMemberDto> UpdateMemberAsync(Guid id, Guid userId, UpdateMemberInput input);

    Task RemoveMemberAsync(Guid id, Guid userId);

    /// <summary>PNG, JPEG, WebP or GIF up to 512 KB.</summary>
    Task<ProjectDto> UploadLogoAsync(Guid id, IRemoteStreamContent file);

    Task<ProjectDto> DeleteLogoAsync(Guid id);

    /// <summary>Makes the space read-only and hides it from default lists. Needs the space's manage permission.</summary>
    Task<ProjectDto> ArchiveAsync(Guid id);

    /// <summary>Brings an archived space back exactly as it was.</summary>
    Task<ProjectDto> RestoreAsync(Guid id);

    /// <summary>
    /// Moves an archived space to the trash; <paramref name="confirmKey"/> must repeat its key. Needs the global
    /// delete permission. The key stays reserved until the space is purged.
    /// </summary>
    Task DeleteAsync(Guid id, string confirmKey);

    /// <summary>Spaces in the trash, for admins.</summary>
    Task<List<TrashedProjectDto>> GetTrashAsync();

    /// <summary>Takes a space out of the trash; it comes back archived.</summary>
    Task<ProjectDto> RestoreDeletedAsync(Guid id);
}

/// <summary>Used by other modules. Every project-scoped call must pass through this.</summary>
public interface IProjectAccess
{
    Task<bool> IsGrantedAsync(Guid projectId, string permission);

    /// <summary>Throws 404 when the caller cannot view the project, 403 when viewing is allowed but the action is not.</summary>
    Task CheckAsync(Guid projectId, string permission);

    /// <summary>The projects the caller can open, deleted ones never; archived ones unless left out.</summary>
    Task<List<Guid>> GetVisibleProjectIdsAsync(bool includeArchived = true);

    /// <summary>
    /// Refuses a write to an archived project. For writes gated by authorship, such as editing one's own comment,
    /// that check viewing only; a write that checks any other key is refused by <see cref="CheckAsync"/> already.
    /// </summary>
    Task CheckWritableAsync(Guid projectId);

    /// <summary>True when the project is archived. Throws when it does not exist.</summary>
    Task<bool> IsArchivedAsync(Guid projectId);

    /// <summary>
    /// For background work with no calling user, such as git events and notifications: true only while the project
    /// exists and is not archived.
    /// </summary>
    Task<bool> IsActiveAsync(Guid projectId);

    Task<ProjectRef> GetRefAsync(Guid projectId);

    Task<string?> GetBranchTemplateAsync(Guid projectId);

    Task<int> GetSprintLengthDaysAsync(Guid projectId);

    Task<ProjectRef?> FindRefByKeyAsync(string key);

    Task<List<ProjectRef>> GetAllRefsAsync();

    Task<Dictionary<Guid, ProjectRef>> GetRefsAsync(IEnumerable<Guid> projectIds);

    Task<bool> IsMemberAsync(Guid projectId, Guid userId);

    /// <summary>Members with their role, for background work that has no calling user.</summary>
    Task<Dictionary<Guid, string>> GetMemberRolesAsync(Guid projectId);

    /// <summary>When each member joined the project.</summary>
    Task<Dictionary<Guid, DateTime>> GetMemberJoinTimesAsync(Guid projectId);

    /// <summary>The caller's role name in the project, null for outsiders.</summary>
    Task<string?> GetCurrentRoleAsync(Guid projectId);

    /// <summary>Takes the next ticket number atomically; numbers are never reused.</summary>
    Task<int> NextTicketNumberAsync(Guid projectId);
}

/// <summary>Adds someone without a calling Project Admin, for invitations already authorized.</summary>
public interface IProjectMembership
{
    /// <summary>Does nothing when the project is gone or archived, or the user already belongs to it.</summary>
    Task AddAsync(Guid projectId, Guid userId, string role, Guid actorId);
}

public record ProjectRef(Guid Id, string Key, string Name, BoardType Type, bool IsArchived = false);

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

/// <summary>
/// Published once a deleted space leaves the trash for good. Every module deletes its own rows for the project,
/// and blobs it stored; handlers must tolerate running twice.
/// </summary>
[Volo.Abp.EventBus.EventName("SprintBoot.Projects.Purged")]
public class ProjectPurgedEto
{
    public Guid ProjectId { get; set; }

    public string Key { get; set; } = null!;
}

public static class ProjectSettings
{
    /// <summary>Days a deleted space stays in the trash before it is purged.</summary>
    public const string TrashRetentionDays = "SprintBoot.Projects.TrashRetentionDays";

    public const int DefaultTrashRetentionDays = 30;
}

public static class ProjectErrors
{
    /// <summary>A write to an archived space; restore it first.</summary>
    public const string Archived = "SprintBoot:SpaceArchived";
    public const string NotArchived = "SprintBoot:SpaceNotArchived";
    public const string ConfirmKeyMismatch = "SprintBoot:SpaceConfirmKeyMismatch";
    public const string KeyTaken = "SprintBoot:ProjectKeyTaken";
    public const string InvalidRole = "SprintBoot:InvalidProjectRole";
    public const string AlreadyMember = "SprintBoot:AlreadyMember";
    public const string LogoInvalid = "SprintBoot:SpaceLogoInvalid";
    public const string LastAdmin = "SprintBoot:LastProjectAdmin";
}
