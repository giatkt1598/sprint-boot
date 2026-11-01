using System.ComponentModel.DataAnnotations;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.Application.Services;

namespace SprintBoot.Agile;

public enum SprintState
{
    Future = 0,
    Active = 1,
    Closed = 2,
}

public class BoardColumnDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required int? MinLimit { get; set; }

    public required int? MaxLimit { get; set; }

    public required bool IsHidden { get; set; }

    public required List<StatusDto> Statuses { get; set; }
}

public class BoardViewDto
{
    public required Guid Id { get; set; }

    public required BoardType Type { get; set; }

    public required SprintSummaryDto? ActiveSprint { get; set; }

    public required List<BoardColumnDto> Columns { get; set; }

    /// <summary>What cards show besides the title, in the keys of <see cref="BoardCardFields"/>.</summary>
    public required List<string> CardFields { get; set; }

    public required List<IssueCardDto> Cards { get; set; }

    /// <summary>Workflow statuses no column shows.</summary>
    public required List<StatusDto> UnmappedStatuses { get; set; }

    /// <summary>Statuses a column still lists that no workflow of the project uses.</summary>
    public required List<StatusDto> StaleStatuses { get; set; }
}

public class SaveColumnInput
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string Name { get; set; }

    [Range(0, 9999)]
    public int? MinLimit { get; set; }

    [Range(0, 9999)]
    public int? MaxLimit { get; set; }

    public bool IsHidden { get; set; }

    public required List<Guid> StatusIds { get; set; }
}

public class SaveBoardInput
{
    public required List<SaveColumnInput> Columns { get; set; }

    /// <summary>Blank keeps the current choice.</summary>
    public List<string>? CardFields { get; set; }
}

public static class BoardCardFields
{
    public static readonly string[] All = ["type", "key", "priority", "assignee", "epic", "labels", "points"];

    public static readonly string[] Default = ["type", "key", "priority", "assignee"];
}

public class SprintSummaryDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string? Goal { get; set; }

    public required SprintState State { get; set; }

    public required DateOnly? StartDate { get; set; }

    public required DateOnly? EndDate { get; set; }

    public required DateTime? CompletedAt { get; set; }

    public required int IssueCount { get; set; }

    public required decimal TotalPoints { get; set; }

    public required decimal DonePoints { get; set; }
}

/// <summary>What a new sprint starts from; everything can still be changed.</summary>
public class SprintDefaultsDto
{
    public required string Name { get; set; }

    public required DateOnly StartDate { get; set; }

    public required DateOnly EndDate { get; set; }

    public required int LengthDays { get; set; }
}

public class SprintWithIssuesDto
{
    public required SprintSummaryDto Sprint { get; set; }

    public required List<IssueCardDto> Issues { get; set; }
}

public class BacklogDto
{
    public required Guid BoardId { get; set; }

    public required BoardType Type { get; set; }

    public required List<SprintWithIssuesDto> Sprints { get; set; }

    public required List<IssueCardDto> Backlog { get; set; }

    public required List<SprintSummaryDto> ClosedSprints { get; set; }
}

public class ClosedSprintDto
{
    public required SprintSummaryDto Sprint { get; set; }

    public required List<IssueCardDto> Completed { get; set; }

    public required List<IssueCardDto> NotCompleted { get; set; }
}

public class SaveSprintInput
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; set; }

    [StringLength(512)]
    public string? Goal { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }
}

public class CreateSprintInput : SaveSprintInput
{
    public required Guid ProjectId { get; set; }
}

public class CompleteSprintInput
{
    /// <summary>Where unfinished issues go. Required as a choice: null is the backlog only with ToBacklog set.</summary>
    public Guid? TargetSprintId { get; set; }

    public bool ToBacklog { get; set; }
}

public class CompleteSprintPreviewDto
{
    public required int DoneCount { get; set; }

    public required int OpenCount { get; set; }
}

public class MoveIssueInput
{
    /// <summary>Destination sprint; null is the backlog.</summary>
    public Guid? SprintId { get; set; }

    /// <summary>Neighbours in the destination list, for rank. Both null keeps the rank.</summary>
    public Guid? AfterIssueId { get; set; }

    public Guid? BeforeIssueId { get; set; }
}

public class RankIssueInput
{
    public Guid? AfterIssueId { get; set; }

    public Guid? BeforeIssueId { get; set; }
}

public class SetBoardEntryInput
{
    public required bool OnBoard { get; set; }
}

public interface IBoardAppService : IApplicationService
{
    Task<BoardViewDto> GetViewAsync(Guid projectId);

    Task<BoardViewDto> UpdateColumnsAsync(Guid projectId, SaveBoardInput input);

    /// <summary>Kanban only: puts an issue on the board or back in the backlog.</summary>
    Task SetEntryAsync(Guid projectId, Guid issueId, SetBoardEntryInput input);

    Task RankAsync(Guid issueId, RankIssueInput input);
}

public interface ISprintAppService : IApplicationService
{
    Task<BacklogDto> GetBacklogAsync(Guid projectId);

    Task<SprintDefaultsDto> GetNewDefaultsAsync(Guid projectId);

    Task<SprintSummaryDto> CreateAsync(CreateSprintInput input);

    Task<SprintSummaryDto> UpdateAsync(Guid id, SaveSprintInput input);

    Task DeleteAsync(Guid id);

    Task<SprintSummaryDto> StartAsync(Guid id, SaveSprintInput input);

    Task<CompleteSprintPreviewDto> GetCompletePreviewAsync(Guid id);

    Task<SprintSummaryDto> CompleteAsync(Guid id, CompleteSprintInput input);

    Task<ClosedSprintDto> GetClosedAsync(Guid id);

    Task MoveIssueAsync(Guid issueId, MoveIssueInput input);

    Task<SprintSummaryDto?> GetForIssueAsync(Guid issueId);
}

[Volo.Abp.EventBus.EventName("SprintBoot.Agile.SprintStateChanged")]
public class SprintStateChangedEto
{
    public Guid EventId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    public string SprintName { get; set; } = string.Empty;

    public SprintState State { get; set; }
}

public static class AgileErrors
{
    public const string ActiveSprintExists = "SprintBoot:ActiveSprintExists";
    public const string SprintDetailsRequired = "SprintBoot:SprintDetailsRequired";
    public const string SprintNotFuture = "SprintBoot:SprintNotFuture";
    public const string SprintNotActive = "SprintBoot:SprintNotActive";
    public const string SprintEndBeforeStart = "SprintBoot:SprintEndBeforeStart";
    public const string CompletionTargetRequired = "SprintBoot:CompletionTargetRequired";
    public const string StatusInTwoColumns = "SprintBoot:StatusInTwoColumns";
    public const string KanbanHasNoSprints = "SprintBoot:KanbanHasNoSprints";
    public const string NotPlannable = "SprintBoot:IssueNotPlannable";
}
