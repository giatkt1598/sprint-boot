using SprintBoot.Identity;
using SprintBoot.Workflows;

namespace SprintBoot.Issues;

public class IssueCardDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required IssueTypeDto Type { get; set; }

    public required StatusDto Status { get; set; }

    public required PriorityDto Priority { get; set; }

    public required UserRefDto? Assignee { get; set; }

    public required UserRefDto? Reporter { get; set; }

    public required string[] Labels { get; set; }

    public required Guid? EpicId { get; set; }

    public required string? EpicTitle { get; set; }

    public required decimal? StoryPoints { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required DateTime UpdatedAt { get; set; }

    public required string ConcurrencyStamp { get; set; }
}

public record IssueFacts(Guid Id, string Key, string Title, Guid? AssigneeId, Guid? ReporterId, IReadOnlyCollection<Guid> Watchers);

/// <summary>What background consumers need to know about an issue. Null when it is gone.</summary>
public interface IIssueFactsProvider
{
    Task<IssueFacts?> FindAsync(Guid issueId);
}

public record IssueRef(Guid IssueId, Guid ProjectId);

/// <summary>For background work with no calling user. Keys that match no issue are left out.</summary>
public interface IIssueKeyResolver
{
    Task<Dictionary<string, IssueRef>> ResolveAsync(IReadOnlyCollection<string> keys);
}

public class WatchersDto
{
    public required int Count { get; set; }

    public required bool WatchedByMe { get; set; }

    public required List<UserRefDto> Users { get; set; }
}

public class EpicRowDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required StatusDto Status { get; set; }

    public required DateOnly? DueDate { get; set; }

    public required UserRefDto? Lead { get; set; }

    public required int Total { get; set; }

    public required int Done { get; set; }

    public required int InProgress { get; set; }
}

/// <summary>Issue data for backlog and board. Results keep rank order; subtasks and epics are never rows.</summary>
public interface IIssuePlanningGateway
{
    Task<List<IssueCardDto>> GetCardsAsync(Guid projectId, IReadOnlyCollection<Guid> issueIds);

    /// <summary>Open issues outside the excluded set, for the backlog.</summary>
    Task<List<IssueCardDto>> GetBacklogCardsAsync(Guid projectId, IReadOnlyCollection<Guid> excludedIssueIds);

    Task<IssueCardDto?> FindCardAsync(Guid issueId);

    Task<Guid> GetProjectIdAsync(Guid issueId);

    /// <summary>Writes only this issue's rank, between its new neighbours.</summary>
    Task MoveRankAsync(Guid issueId, Guid? afterIssueId, Guid? beforeIssueId);

    Task RecordActivityAsync(Guid issueId, string field, string? oldValue, string? newValue);
}
