using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using SprintBoot.Identity;
using SprintBoot.Workflows;
using Volo.Abp.Application.Dtos;

namespace SprintBoot.Issues;

public enum IssueHierarchy
{
    Subtask = 0,
    Standard = 1,
    Epic = 2,
}

public class IssueTypeDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Icon { get; set; }

    public required IssueHierarchy Hierarchy { get; set; }
}

public class PriorityDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required int Order { get; set; }
}

public class IssueRefDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required string TypeIcon { get; set; }

    public required StatusDto Status { get; set; }
}

public class IssueDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required Guid ProjectId { get; set; }

    public required string ProjectKey { get; set; }

    public required string ProjectName { get; set; }

    public required IssueTypeDto Type { get; set; }

    public required string Title { get; set; }

    public required string Description { get; set; }

    public required StatusDto Status { get; set; }

    public required PriorityDto Priority { get; set; }

    public required UserRefDto? Assignee { get; set; }

    public required UserRefDto? Reporter { get; set; }

    public required IssueRefDto? Parent { get; set; }

    public required List<IssueRefDto> Children { get; set; }

    public required string[] Labels { get; set; }

    public required decimal? StoryPoints { get; set; }

    public required DateOnly? StartDate { get; set; }

    public required DateOnly? DueDate { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required DateTime UpdatedAt { get; set; }

    public required int TotalMinutesLogged { get; set; }

    public required string ConcurrencyStamp { get; set; }
}

public class IssueSummaryDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required string ProjectKey { get; set; }

    public required IssueTypeDto Type { get; set; }

    public required StatusDto Status { get; set; }

    public required PriorityDto Priority { get; set; }

    public required UserRefDto? Assignee { get; set; }

    public required UserRefDto? Reporter { get; set; }

    public required string? SprintName { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required DateTime UpdatedAt { get; set; }
}

public class CreateIssueInput
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

    public Guid? ReporterId { get; set; }

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
public class UpdateIssueInput
{
    [Required]
    public required string ConcurrencyStamp { get; set; }

    [Required]
    public required Dictionary<string, JsonElement> Fields { get; set; }
}

public class IssueSearchInput : PagedResultRequestDto
{
    public Guid? ProjectId { get; set; }

    public string? Text { get; set; }

    public Guid[]? TypeIds { get; set; }

    public Guid[]? StatusIds { get; set; }

    public Guid[]? PriorityIds { get; set; }

    public Guid[]? AssigneeIds { get; set; }

    public Guid[]? ReporterIds { get; set; }

    public Guid[]? ParentIds { get; set; }

    public string[]? Labels { get; set; }

    public DateTime? CreatedFrom { get; set; }

    public DateTime? CreatedTo { get; set; }

    public DateTime? UpdatedFrom { get; set; }

    public DateTime? UpdatedTo { get; set; }
}

public class IssueSearchResultDto : PagedResultDto<IssueSummaryDto>
{
    /// <summary>Set when the text is exactly an issue key the caller may open.</summary>
    public string? ExactKey { get; set; }
}

public class DeletedIssueDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required string ProjectName { get; set; }

    public required int SubtaskCount { get; set; }

    public required UserRefDto? DeletedBy { get; set; }

    public required DateTime DeletedAt { get; set; }
}

public class IssueActivityDto
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
}

public class CreateCommentInput
{
    [Required]
    public required Guid IssueId { get; set; }

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
    public required Guid IssueId { get; set; }

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

public record ProjectIssueStats(int Count, DateTime UpdatedAt);

public interface IIssueProjectStats
{
    Task<Dictionary<Guid, ProjectIssueStats>> GetAsync(IEnumerable<Guid> projectIds);
}

public static class IssueErrors
{
    public const string ProjectMismatch = "SprintBoot:IssueProjectMismatch";
    public const string ParentRequired = "SprintBoot:SubtaskParentRequired";
    public const string InvalidHierarchy = "SprintBoot:InvalidIssueHierarchy";
    public const string DueBeforeStart = "SprintBoot:DueDateBeforeStartDate";
    public const string UnknownField = "SprintBoot:UnknownIssueField";
    public const string FieldNotWritable = "SprintBoot:IssueFieldNotWritable";
    public const string InvalidFieldValue = "SprintBoot:InvalidIssueFieldValue";
    public const string TitleRequired = "SprintBoot:IssueTitleRequired";
    public const string AttachmentTooLarge = "SprintBoot:AttachmentTooLarge";
    public const string WorkLogInvalidTime = "SprintBoot:WorkLogInvalidTime";
    public const string ReplyDepth = "SprintBoot:CommentReplyDepth";
    public const string CommentEmpty = "SprintBoot:CommentEmpty";
}
