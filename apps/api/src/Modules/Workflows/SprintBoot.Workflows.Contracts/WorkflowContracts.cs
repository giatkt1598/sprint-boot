using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Volo.Abp.Application.Services;
using Volo.Abp.EventBus;

namespace SprintBoot.Workflows;

public enum RuleKind
{
    Condition = 0,
    Validator = 1,
    PostFunction = 2,
}

public enum WorkflowVersionState
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

public class TransitionRuleDto
{
    public required RuleKind Kind { get; set; }

    public required string Key { get; set; }

    public JsonElement? Config { get; set; }
}

public class TransitionDefinitionDto
{
    [Required]
    public required string Name { get; set; }

    /// <summary>Null means Any status.</summary>
    public Guid? FromStatusId { get; set; }

    public required Guid ToStatusId { get; set; }

    public List<TransitionRuleDto> Rules { get; set; } = [];
}

public class WorkflowDefinitionDto
{
    public required Guid InitialStatusId { get; set; }

    public required List<Guid> StatusIds { get; set; }

    public required List<TransitionDefinitionDto> Transitions { get; set; }

    public string? LayoutJson { get; set; }
}

public class WorkflowSummaryDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required int StatusCount { get; set; }

    public required int SpaceCount { get; set; }

    public required int? Version { get; set; }

    public required bool HasDraft { get; set; }

    public required DateTime UpdatedAt { get; set; }
}

public class WorkflowDetailDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string? Description { get; set; }

    public required int? Version { get; set; }

    public required WorkflowDefinitionDto? Published { get; set; }

    public required WorkflowDefinitionDto? Draft { get; set; }
}

public class CreateWorkflowInput
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; set; }

    [StringLength(512)]
    public string? Description { get; set; }

    /// <summary>Start the draft as a copy of this workflow.</summary>
    public Guid? CopyFromId { get; set; }

    public WorkflowDefinitionDto? Definition { get; set; }
}

public class PublishWorkflowInput
{
    /// <summary>Old status id to replacement status id, for statuses the new version drops.</summary>
    public Dictionary<Guid, Guid> StatusMappings { get; set; } = [];
}

public class PublishResultDto
{
    public required int Version { get; set; }

    /// <summary>True when issues are moved in the background because there are many.</summary>
    public required bool MigrationQueued { get; set; }
}

public class ProjectWorkflowDto
{
    public required Guid IssueTypeId { get; set; }

    public required Guid WorkflowId { get; set; }

    public required string WorkflowName { get; set; }
}

public class SetProjectWorkflowInput
{
    public required Guid ProjectId { get; set; }

    public required Guid IssueTypeId { get; set; }

    public required Guid WorkflowId { get; set; }

    public Dictionary<Guid, Guid> StatusMappings { get; set; } = [];
}

public class AvailableTransitionDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required StatusDto ToStatus { get; set; }

    public required bool RequiresComment { get; set; }

    /// <summary>Issue fields the validators require and that are still empty.</summary>
    public required string[] MissingFields { get; set; }

    /// <summary>Validator problems the dialog cannot fix, shown as the reason it is blocked.</summary>
    public required string[] Problems { get; set; }
}

public class ExecuteTransitionInput
{
    public required Guid TransitionId { get; set; }

    [Required]
    public required string ConcurrencyStamp { get; set; }

    public string? Comment { get; set; }

    /// <summary>Values entered in the transition dialog, by issue field key.</summary>
    public Dictionary<string, JsonElement> Fields { get; set; } = [];
}

public interface IWorkflowAppService : IApplicationService
{
    Task<List<WorkflowSummaryDto>> GetListAsync();

    Task<WorkflowDetailDto> GetAsync(Guid id);

    Task<WorkflowDetailDto> CreateAsync(CreateWorkflowInput input);

    Task<WorkflowDetailDto> SaveDraftAsync(Guid id, WorkflowDefinitionDto input);

    Task<PublishResultDto> PublishAsync(Guid id, PublishWorkflowInput input);
}

public interface IProjectWorkflowAppService : IApplicationService
{
    Task<List<ProjectWorkflowDto>> GetListAsync(Guid projectId);

    Task<PublishResultDto> SetAsync(SetProjectWorkflowInput input);
}

public interface ITransitionAppService : IApplicationService
{
    Task<List<AvailableTransitionDto>> GetAvailableAsync(Guid issueId);

    Task ExecuteAsync(Guid issueId, ExecuteTransitionInput input);
}

public record InitialState(Guid StatusId, Guid VersionId);

/// <summary>Used by Issues to start a new issue in the right workflow.</summary>
public interface IWorkflowRuntime
{
    Task<InitialState> GetInitialStateAsync(Guid projectId, Guid issueTypeId);

    /// <summary>Every status the project's published workflows use, for board columns.</summary>
    Task<List<Guid>> GetProjectStatusIdsAsync(Guid projectId);
}

public record TransitionSubject(
    Guid IssueId,
    string Key,
    Guid ProjectId,
    Guid TypeId,
    Guid StatusId,
    Guid? WorkflowVersionId,
    Guid? AssigneeId,
    Guid? ReporterId,
    string ConcurrencyStamp,
    IReadOnlyDictionary<string, bool> FieldHasValue);

/// <summary>Selects issues to move between workflow versions.</summary>
public record IssueScope(Guid[] VersionIds, Guid? ProjectId = null, Guid? IssueTypeId = null);

/// <summary>
/// Implemented by Issues. This is the only path that changes an issue's status,
/// and Workflows reaches issue data through it instead of touching their tables.
/// </summary>
public interface IWorkflowIssueGateway
{
    Task<TransitionSubject> GetSubjectAsync(Guid issueId);

    Task<List<Guid>> GetSubtaskStatusIdsAsync(Guid issueId);

    Task MoveAsync(Guid issueId, Guid statusId, string fromName, string toName);

    Task SetFieldAsync(Guid issueId, string field, JsonElement value);

    Task SetResolutionAsync(Guid issueId, string? resolution);

    Task AddCommentAsync(Guid issueId, string body);

    Task<Dictionary<Guid, int>> CountByStatusAsync(IssueScope? scope = null);

    Task<int> CountPendingAsync(IssueScope scope, Guid toVersionId);

    /// <summary>Moves up to batchSize issues to the version and returns how many moved.</summary>
    Task<int> MigrateBatchAsync(IssueScope scope, Guid toVersionId, IReadOnlyDictionary<Guid, Guid> statusMappings, int batchSize);
}

/// <summary>Raised by the Send notification post-function; recipients come from the scheme.</summary>
[EventName("SprintBoot.Workflows.WorkflowNotification")]
public class WorkflowNotificationEto
{
    public Guid EventId { get; set; }

    public Guid IssueId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    public string? Message { get; set; }
}

[EventName("SprintBoot.Workflows.IssueTransitioned")]
public class IssueTransitionedEto
{
    public Guid EventId { get; set; }

    public Guid ActorId { get; set; }

    public string ToStatusName { get; set; } = string.Empty;

    public Guid IssueId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid FromStatusId { get; set; }

    public Guid ToStatusId { get; set; }
}

public static class WorkflowErrors
{
    public const string InvalidTransition = "SprintBoot:InvalidTransition";
    public const string ConditionFailed = "SprintBoot:TransitionConditionFailed";
    public const string ValidationFailed = "SprintBoot:TransitionValidationFailed";
    public const string CommentRequired = "SprintBoot:TransitionCommentRequired";
    public const string InvalidDefinition = "SprintBoot:InvalidWorkflowDefinition";
    public const string UnreachableStatus = "SprintBoot:UnreachableStatus";
    public const string NoDoneStatus = "SprintBoot:WorkflowNeedsDoneStatus";
    public const string UnknownRule = "SprintBoot:UnknownWorkflowRule";
    public const string StatusMappingRequired = "SprintBoot:StatusMappingRequired";
    public const string NoDraft = "SprintBoot:WorkflowHasNoDraft";
    public const string NotPublished = "SprintBoot:WorkflowNotPublished";
    public const string MissingField = "SprintBoot:TransitionMissingField";
}
