using SprintBoot.Identity;
using SprintBoot.Workflows;
using Volo.Abp.Application.Services;

namespace SprintBoot.Issues;

/// <summary>One issue as the space overview counts it.</summary>
/// <param name="CompletedAt">When a done issue last changed status, so roughly when it was finished; null while it is open.</param>
public record IssueOverviewRow(
    Guid Id,
    Guid? ParentId,
    string TypeName,
    string TypeIcon,
    IssueHierarchy Hierarchy,
    StatusCategory Category,
    string StatusName,
    Guid? AssigneeId,
    decimal? StoryPoints,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt);

public enum IssueEventKind
{
    Created = 0,
    Changed = 1,
    Commented = 2,
}

/// <summary>Something a person did to an issue of the space.</summary>
/// <param name="Field">The field a change touched, as the issue history names it.</param>
/// <param name="NewValue">Kept only for a status change (the status name) and for logged work (minutes).</param>
public record IssueEventRow(Guid ActorId, IssueEventKind Kind, string IssueKey, string IssueTitle, string? Field, string? NewValue, DateTime At);

/// <summary>What the space overview needs from issues, without touching their tables.</summary>
public interface IIssueOverviewFacts
{
    /// <summary>Every issue of the space, epics and subtasks included.</summary>
    Task<List<IssueOverviewRow>> GetIssuesAsync(Guid projectId);

    /// <summary>The latest creations, changes and comments, newest first.</summary>
    Task<List<IssueEventRow>> GetRecentEventsAsync(Guid projectId, int take);
}

public class ProjectIntroductionDto
{
    /// <summary>Sanitized rich text HTML; empty until someone writes it.</summary>
    public required string Content { get; set; }

    public required UserRefDto? UpdatedBy { get; set; }

    public required DateTime? UpdatedAt { get; set; }

    /// <summary>Whether the reader may change it.</summary>
    public required bool CanEdit { get; set; }
}

public class UpdateProjectIntroductionInput
{
    public string? Content { get; set; }
}

/// <summary>The page that introduces a space. Readers of its issues read it; its admins write it.</summary>
public interface IProjectIntroductionAppService : IApplicationService
{
    Task<ProjectIntroductionDto> GetAsync(Guid projectId);

    Task<ProjectIntroductionDto> UpdateAsync(Guid projectId, UpdateProjectIntroductionInput input);
}
