using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using SprintBoot.Identity;
using SprintBoot.Workflows;
using Volo.Abp.Application.Dtos;

namespace SprintBoot.Tickets;

public enum TicketHierarchy
{
    Subtask = 0,
    Standard = 1,
    Epic = 2,
}

public class TicketTypeDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Icon { get; set; }

    public required TicketHierarchy Hierarchy { get; set; }
}

public class PriorityDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required int Order { get; set; }
}

public class TicketRefDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required string TypeIcon { get; set; }

    public required StatusDto Status { get; set; }

    public required UserRefDto? Assignee { get; set; }

    public required string ConcurrencyStamp { get; set; }
}

public class TicketDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required Guid ProjectId { get; set; }

    public required string ProjectKey { get; set; }

    public required string ProjectName { get; set; }

    public required TicketTypeDto Type { get; set; }

    public required string Title { get; set; }

    public required string Description { get; set; }

    public required StatusDto Status { get; set; }

    public required PriorityDto Priority { get; set; }

    public required UserRefDto? Assignee { get; set; }

    public required UserRefDto? Reporter { get; set; }

    public required TicketRefDto? Parent { get; set; }

    public required List<TicketRefDto> Children { get; set; }

    public required string[] Labels { get; set; }

    public required decimal? StoryPoints { get; set; }

    public required DateOnly? StartDate { get; set; }

    public required DateOnly? DueDate { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required DateTime UpdatedAt { get; set; }

    public required int TotalMinutesLogged { get; set; }

    public required string ConcurrencyStamp { get; set; }
}

public class TicketSummaryDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required string ProjectKey { get; set; }

    public required TicketTypeDto Type { get; set; }

    public required StatusDto Status { get; set; }

    public required PriorityDto Priority { get; set; }

    public required UserRefDto? Assignee { get; set; }

    public required UserRefDto? Reporter { get; set; }

    public required string? SprintName { get; set; }

    /// <summary>The epic of a standard ticket, or the ticket a subtask belongs to.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>The epic above the ticket: its parent, or for a subtask its parent's parent.</summary>
    public Guid? EpicId { get; set; }

    public string? EpicTitle { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required DateTime UpdatedAt { get; set; }

    public required string ConcurrencyStamp { get; set; }
}

public class CreateTicketInput
{
    [Required]
    public required Guid ProjectId { get; set; }

    [Required]
    public required Guid TypeId { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Title { get; set; }

    public string? Description { get; set; }

    public Guid? PriorityId { get; set; }

    public Guid? AssigneeId { get; set; }

    public Guid? ParentId { get; set; }

    public string[]? Labels { get; set; }

    [Range(0, 9999)]
    public decimal? StoryPoints { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? DueDate { get; set; }
}

/// <summary>
/// Partial update by field key, so custom fields can join later. Status is deliberately
/// not an editable field: it only changes through a workflow transition.
/// </summary>
public class UpdateTicketInput
{
    [Required]
    public required string ConcurrencyStamp { get; set; }

    [Required]
    public required Dictionary<string, JsonElement> Fields { get; set; }
}

public class TicketSearchInput : PagedResultRequestDto, ISortedResultRequest
{
    /// <summary>
    /// A column and an optional direction, such as "priority desc": key, title, status, priority, assignee,
    /// reporter, created or updated. Empty means most recently updated first.
    /// </summary>
    public string? Sorting { get; set; }

    public Guid? ProjectId { get; set; }

    public string? Text { get; set; }

    /// <summary>Leaves out tickets of archived spaces, for pickers that would change them.</summary>
    public bool ExcludeArchived { get; set; }

    public Guid[]? TypeIds { get; set; }

    public Guid[]? StatusIds { get; set; }

    public Guid[]? PriorityIds { get; set; }

    public Guid[]? AssigneeIds { get; set; }

    public Guid[]? ReporterIds { get; set; }

    public Guid[]? ParentIds { get; set; }

    /// <summary>Tickets of these sprints, as their board and backlog show them.</summary>
    public Guid[]? SprintIds { get; set; }

    public string[]? Labels { get; set; }

    public DateTime? CreatedFrom { get; set; }

    public DateTime? CreatedTo { get; set; }

    public DateTime? UpdatedFrom { get; set; }

    public DateTime? UpdatedTo { get; set; }
}

public class TicketSearchResultDto : PagedResultDto<TicketSummaryDto>
{
    /// <summary>Set when the text is exactly a ticket key the caller may open.</summary>
    public string? ExactKey { get; set; }
}

public class DeletedTicketDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required string ProjectName { get; set; }

    public required int SubtaskCount { get; set; }

    public required UserRefDto? DeletedBy { get; set; }

    public required DateTime DeletedAt { get; set; }
}

public class TicketActivityDto
{
    public required Guid Id { get; set; }

    public required UserRefDto? Actor { get; set; }

    public required ActorType ActorType { get; set; }

    public required string Field { get; set; }

    public required string? OldValue { get; set; }

    public required string? NewValue { get; set; }

    public required DateTime CreatedAt { get; set; }
}

public class CommentDto
{
    public required Guid Id { get; set; }

    public required Guid? ParentId { get; set; }

    public required UserRefDto? Author { get; set; }

    public required string Body { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required bool IsEdited { get; set; }

    public required List<CommentReactionDto> Reactions { get; set; }
}

public class CommentReactionDto
{
    public required string Emoji { get; set; }

    public required int Count { get; set; }

    public required bool ReactedByMe { get; set; }

    /// <summary>Who reacted, earliest first. People no longer in the directory are left out, so it may be shorter than Count.</summary>
    public required List<UserRefDto> Users { get; set; }
}

public class CreateCommentInput
{
    [Required]
    public required Guid TicketId { get; set; }

    public Guid? ParentId { get; set; }

    [Required]
    public required string Body { get; set; }
}

public class UpdateCommentInput
{
    [Required]
    public required string Body { get; set; }
}

public class ToggleReactionInput
{
    [Required]
    [StringLength(16)]
    public required string Emoji { get; set; }
}

public class AttachmentDto
{
    public required Guid Id { get; set; }

    public required string FileName { get; set; }

    public required long Size { get; set; }

    public required string ContentType { get; set; }

    public required UserRefDto? UploadedBy { get; set; }

    public required DateTime CreatedAt { get; set; }
}

public class WorkLogDto
{
    public required Guid Id { get; set; }

    public required UserRefDto? Author { get; set; }

    public required int Minutes { get; set; }

    public required DateOnly WorkDate { get; set; }

    public required string? Note { get; set; }
}

public class WorkLogListDto
{
    public required int TotalMinutes { get; set; }

    public required List<WorkLogDto> Items { get; set; }
}

public class CreateWorkLogInput
{
    [Required]
    public required Guid TicketId { get; set; }

    public int Minutes { get; set; }

    public DateOnly WorkDate { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}

public class UpdateWorkLogInput
{
    public int Minutes { get; set; }

    public DateOnly WorkDate { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}

public record ProjectTicketStats(int Count, DateTime UpdatedAt);

public interface ITicketProjectStats
{
    Task<Dictionary<Guid, ProjectTicketStats>> GetAsync(IEnumerable<Guid> projectIds);
}

public static class TicketErrors
{
    public const string ProjectMismatch = "SprintBoot:TicketProjectMismatch";
    public const string ParentRequired = "SprintBoot:SubtaskParentRequired";
    public const string InvalidHierarchy = "SprintBoot:InvalidTicketHierarchy";
    public const string DueBeforeStart = "SprintBoot:DueDateBeforeStartDate";
    public const string UnknownField = "SprintBoot:UnknownTicketField";
    public const string FieldNotWritable = "SprintBoot:TicketFieldNotWritable";
    public const string InvalidFieldValue = "SprintBoot:InvalidTicketFieldValue";
    public const string TitleRequired = "SprintBoot:TicketTitleRequired";
    public const string AttachmentTooLarge = "SprintBoot:AttachmentTooLarge";
    public const string WorkLogInvalidTime = "SprintBoot:WorkLogInvalidTime";
    public const string ReplyDepth = "SprintBoot:CommentReplyDepth";
    public const string CommentEmpty = "SprintBoot:CommentEmpty";
    public const string InvalidSorting = "SprintBoot:InvalidTicketSorting";
}
