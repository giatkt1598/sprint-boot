using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;

namespace SprintBoot.Workflows;

[Authorize]
public class StatusAppService(
    IRepository<Status, Guid> statuses,
    IRepository<WorkflowStep, Guid> steps,
    IRepository<WorkflowVersion, Guid> versions,
    IWorkflowIssueGateway issues)
    : ApplicationService, IStatusAppService
{
    public async Task<List<StatusDto>> GetListAsync()
    {
        var items = await statuses.GetListAsync();
        return [.. items.OrderBy(status => status.Order).Select(StatusMapping.ToDto)];
    }

    [Authorize(SprintBootPermissions.ManageWorkflows)]
    public async Task<List<StatusAdminDto>> GetAdminListAsync()
    {
        var items = await statuses.GetListAsync();
        var active = (await versions.GetListAsync(version => version.State == WorkflowVersionState.Published));
        var activeIds = active.Select(version => version.Id).ToList();
        var activeSteps = await steps.GetListAsync(step => activeIds.Contains(step.VersionId));
        var counts = await issues.CountByStatusAsync();

        return [.. items.OrderBy(status => status.Order).Select(status => new StatusAdminDto
        {
            Id = status.Id,
            Name = status.Name,
            Category = status.Category,
            WorkflowCount = activeSteps.Count(step => step.StatusId == status.Id),
            IssueCount = counts.GetValueOrDefault(status.Id),
            UpdatedAt = status.LastModificationTime ?? status.CreationTime,
        })];
    }

    [Authorize(SprintBootPermissions.ManageWorkflows)]
    public async Task<StatusDto> CreateAsync(SaveStatusInput input)
    {
        var order = (await statuses.GetCountAsync()) + 1;
        var status = await statuses.InsertAsync(new Status(GuidGenerator.Create(), input.Name, input.Category, (int)order));
        return StatusMapping.ToDto(status);
    }

    [Authorize(SprintBootPermissions.ManageWorkflows)]
    public async Task<StatusDto> UpdateAsync(Guid id, SaveStatusInput input)
    {
        var status = await statuses.GetAsync(id);
        status.Name = input.Name;
        status.Category = input.Category;
        await statuses.UpdateAsync(status);
        return StatusMapping.ToDto(status);
    }
}

public class StatusLookup(IRepository<Status, Guid> statuses) : IStatusLookup, ITransientDependency
{
    public async Task<List<StatusDto>> GetListAsync()
    {
        var items = await statuses.GetListAsync();
        return [.. items.OrderBy(status => status.Order).Select(StatusMapping.ToDto)];
    }
}

public static class StatusMapping
{
    public static StatusDto ToDto(Status status) => new()
    {
        Id = status.Id,
        Name = status.Name,
        Category = status.Category,
    };
}

public class WorkflowStore(
    IRepository<WorkflowVersion, Guid> versions,
    IRepository<WorkflowStep, Guid> steps,
    IRepository<Transition, Guid> transitions,
    IRepository<TransitionRule, Guid> rules,
    IGuidGenerator guids) : ITransientDependency
{
    public async Task<WorkflowVersion?> FindPublishedAsync(Guid workflowId) =>
        await versions.FirstOrDefaultAsync(version => version.WorkflowId == workflowId && version.State == WorkflowVersionState.Published);

    public async Task<WorkflowVersion?> FindDraftAsync(Guid workflowId) =>
        await versions.FirstOrDefaultAsync(version => version.WorkflowId == workflowId && version.State == WorkflowVersionState.Draft);

    public async Task<WorkflowDefinitionDto> LoadAsync(WorkflowVersion version)
    {
        var stepList = await steps.GetListAsync(step => step.VersionId == version.Id);
        var transitionList = await transitions.GetListAsync(transition => transition.VersionId == version.Id);
        var ids = transitionList.Select(transition => transition.Id).ToList();
        var ruleList = await rules.GetListAsync(rule => ids.Contains(rule.TransitionId));

        return new WorkflowDefinitionDto
        {
            InitialStatusId = version.InitialStatusId,
            LayoutJson = version.LayoutJson,
            StatusIds = [.. stepList.Select(step => step.StatusId)],
            Transitions = [.. transitionList.Select(transition => new TransitionDefinitionDto
            {
                Name = transition.Name,
                FromStatusId = transition.FromStatusId,
                ToStatusId = transition.ToStatusId,
                Rules = [.. ruleList.Where(rule => rule.TransitionId == transition.Id).OrderBy(rule => rule.Order).Select(ToDto)],
            })],
        };
    }

    public static TransitionRuleDto ToDto(TransitionRule rule) => new()
    {
        Kind = rule.Kind,
        Key = rule.RuleKey,
        Config = rule.ConfigJson is null ? null : JsonDocument.Parse(rule.ConfigJson).RootElement,
    };

    public async Task WriteAsync(WorkflowVersion version, WorkflowDefinitionDto definition)
    {
        await ClearAsync(version.Id);
        version.InitialStatusId = definition.InitialStatusId;
        version.LayoutJson = definition.LayoutJson;

        await steps.InsertManyAsync(definition.StatusIds.Distinct()
            .Select(statusId => new WorkflowStep(guids.Create(), version.Id, statusId)));

        foreach (var item in definition.Transitions)
        {
            var transition = await transitions.InsertAsync(
                new Transition(guids.Create(), version.Id, item.Name, item.FromStatusId, item.ToStatusId));
            await rules.InsertManyAsync(item.Rules.Select((rule, order) => new TransitionRule(
                guids.Create(), transition.Id, rule.Kind, rule.Key, order, rule.Config?.GetRawText())));
        }
    }

    private async Task ClearAsync(Guid versionId)
    {
        var existing = await transitions.GetListAsync(transition => transition.VersionId == versionId);
        var ids = existing.Select(transition => transition.Id).ToList();
        await rules.DeleteAsync(rule => ids.Contains(rule.TransitionId), autoSave: true);
        await transitions.DeleteAsync(transition => transition.VersionId == versionId, autoSave: true);
        await steps.DeleteAsync(step => step.VersionId == versionId, autoSave: true);
    }
}

/// <summary>Rejects definitions that would strand issues or reference unknown things.</summary>
public class WorkflowDefinitionValidator(IRepository<Status, Guid> statuses, RuleRegistry registry) : ITransientDependency
{
    public async Task EnsureValidAsync(WorkflowDefinitionDto definition)
    {
        var steps = definition.StatusIds.ToHashSet();
        var known = (await statuses.GetListAsync()).ToDictionary(status => status.Id);

        if (!steps.Contains(definition.InitialStatusId) || !steps.All(known.ContainsKey))
        {
            throw new BusinessException(WorkflowErrors.InvalidDefinition);
        }

        if (definition.Transitions.Any(transition =>
                !steps.Contains(transition.ToStatusId)
                || (transition.FromStatusId is { } from && !steps.Contains(from))
                || string.IsNullOrWhiteSpace(transition.Name)))
        {
            throw new BusinessException(WorkflowErrors.InvalidDefinition);
        }

        if (!steps.Any(id => known[id].Category == StatusCategory.Done))
        {
            throw new BusinessException(WorkflowErrors.NoDoneStatus);
        }

        foreach (var rule in definition.Transitions.SelectMany(transition => transition.Rules))
        {
            if (!registry.Exists(rule.Kind, rule.Key))
            {
                throw new BusinessException(WorkflowErrors.UnknownRule).WithData("rule", rule.Key);
            }
        }

        var unreachable = steps.Except(Reachable(definition)).ToList();
        if (unreachable.Count > 0)
        {
            throw new BusinessException(WorkflowErrors.UnreachableStatus)
                .WithData("statuses", string.Join(", ", unreachable.Select(id => known[id].Name)));
        }
    }

    private static HashSet<Guid> Reachable(WorkflowDefinitionDto definition)
    {
        var reached = new HashSet<Guid> { definition.InitialStatusId };
        var queue = new Queue<Guid>(reached);
        while (queue.TryDequeue(out var current))
        {
            foreach (var transition in definition.Transitions.Where(item => item.FromStatusId is null || item.FromStatusId == current))
            {
                if (reached.Add(transition.ToStatusId))
                {
                    queue.Enqueue(transition.ToStatusId);
                }
            }
        }

        return reached;
    }
}

/// <summary>Moves issues to a version: inline when few, queued when many.</summary>
public class IssueMigrator(
    IWorkflowIssueGateway issues,
    IRepository<WorkflowMigration, Guid> migrations,
    IGuidGenerator guids,
    IConfiguration configuration) : ITransientDependency
{
    public const int BatchSize = 500;

    public async Task<bool> RunAsync(IssueScope scope, Guid toVersionId, HashSet<Guid> newSteps, Dictionary<Guid, Guid> mappings)
    {
        var usage = await issues.CountByStatusAsync(scope);
        var orphaned = usage.Where(item => item.Value > 0 && !newSteps.Contains(item.Key)).Select(item => item.Key).ToList();
        var unmapped = orphaned.Where(id => !mappings.TryGetValue(id, out var target) || !newSteps.Contains(target)).ToList();
        if (unmapped.Count > 0)
        {
            throw new BusinessException(WorkflowErrors.StatusMappingRequired)
                .WithData("statuses", string.Join(",", unmapped));
        }

        var pending = await issues.CountPendingAsync(scope, toVersionId);
        if (pending <= configuration.GetValue("Workflows:Migration:SyncLimit", 1000))
        {
            while (await issues.MigrateBatchAsync(scope, toVersionId, mappings, BatchSize) > 0)
            {
            }

            return false;
        }

        await migrations.InsertAsync(new WorkflowMigration(
            guids.Create(), JsonSerializer.Serialize(scope), toVersionId, JsonSerializer.Serialize(mappings)));
        return true;
    }
}

[Authorize(SprintBootPermissions.ManageWorkflows)]
public class WorkflowAppService(
    IRepository<Workflow, Guid> workflows,
    IRepository<WorkflowVersion, Guid> versions,
    IRepository<WorkflowStep, Guid> steps,
    IRepository<ProjectWorkflowMapping, Guid> mappings,
    WorkflowStore store,
    WorkflowDefinitionValidator validator,
    IssueMigrator migrator)
    : ApplicationService, IWorkflowAppService
{
    public async Task<List<WorkflowSummaryDto>> GetListAsync()
    {
        var result = new List<WorkflowSummaryDto>();
        foreach (var workflow in (await workflows.GetListAsync()).OrderBy(item => item.Name))
        {
            var published = await store.FindPublishedAsync(workflow.Id);
            var draft = await store.FindDraftAsync(workflow.Id);
            var current = published ?? draft;
            var statusCount = current is null ? 0 : await steps.CountAsync(step => step.VersionId == current.Id);
            var spaces = (await mappings.GetListAsync(mapping => mapping.WorkflowId == workflow.Id))
                .Select(mapping => mapping.ProjectId).Distinct().Count();

            result.Add(new WorkflowSummaryDto
            {
                Id = workflow.Id,
                Name = workflow.Name,
                StatusCount = statusCount,
                SpaceCount = spaces,
                Version = published?.VersionNumber,
                HasDraft = draft is not null,
                UpdatedAt = workflow.LastModificationTime ?? workflow.CreationTime,
            });
        }

        return result;
    }

    public async Task<WorkflowDetailDto> GetAsync(Guid id)
    {
        var workflow = await workflows.GetAsync(id);
        var published = await store.FindPublishedAsync(id);
        var draft = await store.FindDraftAsync(id);
        return new WorkflowDetailDto
        {
            Id = workflow.Id,
            Name = workflow.Name,
            Description = workflow.Description,
            Version = published?.VersionNumber,
            Published = published is null ? null : await store.LoadAsync(published),
            Draft = draft is null ? null : await store.LoadAsync(draft),
        };
    }

    public async Task<WorkflowDetailDto> CreateAsync(CreateWorkflowInput input)
    {
        var definition = input.Definition;
        if (input.CopyFromId is { } sourceId)
        {
            var source = await store.FindPublishedAsync(sourceId) ?? throw new BusinessException(WorkflowErrors.NotPublished);
            definition = await store.LoadAsync(source);
        }

        if (definition is null)
        {
            throw new BusinessException(WorkflowErrors.InvalidDefinition);
        }

        var workflow = await workflows.InsertAsync(new Workflow(GuidGenerator.Create(), input.Name, input.Description), autoSave: true);
        var version = await versions.InsertAsync(
            new WorkflowVersion(GuidGenerator.Create(), workflow.Id, 1, definition.InitialStatusId, definition.LayoutJson), autoSave: true);
        await store.WriteAsync(version, definition);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await GetAsync(workflow.Id);
    }

    public async Task<WorkflowDetailDto> SaveDraftAsync(Guid id, WorkflowDefinitionDto input)
    {
        _ = await workflows.GetAsync(id);
        var draft = await store.FindDraftAsync(id);
        if (draft is null)
        {
            var numbers = await versions.GetListAsync(version => version.WorkflowId == id);
            draft = await versions.InsertAsync(new WorkflowVersion(
                GuidGenerator.Create(), id, numbers.Max(version => version.VersionNumber) + 1, input.InitialStatusId, input.LayoutJson), autoSave: true);
        }

        await store.WriteAsync(draft, input);
        await versions.UpdateAsync(draft, autoSave: true);
        return await GetAsync(id);
    }

    public async Task<PublishResultDto> PublishAsync(Guid id, PublishWorkflowInput input)
    {
        var draft = await store.FindDraftAsync(id) ?? throw new BusinessException(WorkflowErrors.NoDraft);
        var definition = await store.LoadAsync(draft);
        await validator.EnsureValidAsync(definition);

        var previous = await versions.GetListAsync(version => version.WorkflowId == id && version.Id != draft.Id);
        var scope = new IssueScope([.. previous.Select(version => version.Id)]);
        var queued = previous.Count > 0
            && await migrator.RunAsync(scope, draft.Id, [.. definition.StatusIds], input.StatusMappings);

        foreach (var version in previous.Where(version => version.State == WorkflowVersionState.Published))
        {
            version.State = WorkflowVersionState.Archived;
            await versions.UpdateAsync(version);
        }

        draft.State = WorkflowVersionState.Published;
        await versions.UpdateAsync(draft);

        var workflow = await workflows.GetAsync(id);
        await workflows.UpdateAsync(workflow);
        return new PublishResultDto { Version = draft.VersionNumber, MigrationQueued = queued };
    }
}

[Authorize]
public class ProjectWorkflowAppService(
    IRepository<ProjectWorkflowMapping, Guid> mappings,
    IRepository<Workflow, Guid> workflows,
    IRepository<WorkflowVersion, Guid> versions,
    IProjectAccess projectAccess,
    WorkflowStore store,
    IssueMigrator migrator)
    : ApplicationService, IProjectWorkflowAppService
{
    public async Task<List<ProjectWorkflowDto>> GetListAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var list = await mappings.GetListAsync(mapping => mapping.ProjectId == projectId);
        var ids = list.Select(mapping => mapping.WorkflowId).ToList();
        var names = (await workflows.GetListAsync(workflow => ids.Contains(workflow.Id))).ToDictionary(workflow => workflow.Id, workflow => workflow.Name);
        return [.. list.Select(mapping => new ProjectWorkflowDto
        {
            IssueTypeId = mapping.IssueTypeId,
            WorkflowId = mapping.WorkflowId,
            WorkflowName = names[mapping.WorkflowId],
        })];
    }

    public async Task<PublishResultDto> SetAsync(SetProjectWorkflowInput input)
    {
        await projectAccess.CheckAsync(input.ProjectId, ProjectPermissions.Manage);

        var target = await store.FindPublishedAsync(input.WorkflowId) ?? throw new BusinessException(WorkflowErrors.NotPublished);
        var definition = await store.LoadAsync(target);

        // Every older version of any workflow may hold this project's issues.
        var all = await versions.GetListAsync(version => version.Id != target.Id);
        var scope = new IssueScope([.. all.Select(version => version.Id)], input.ProjectId, input.IssueTypeId);
        var queued = await migrator.RunAsync(scope, target.Id, [.. definition.StatusIds], input.StatusMappings);

        var mapping = await mappings.FirstOrDefaultAsync(item => item.ProjectId == input.ProjectId && item.IssueTypeId == input.IssueTypeId);
        if (mapping is null)
        {
            await mappings.InsertAsync(new ProjectWorkflowMapping(GuidGenerator.Create(), input.ProjectId, input.IssueTypeId, input.WorkflowId));
        }
        else
        {
            mapping.WorkflowId = input.WorkflowId;
            await mappings.UpdateAsync(mapping);
        }

        return new PublishResultDto { Version = target.VersionNumber, MigrationQueued = queued };
    }
}

public class WorkflowRuntime(IRepository<ProjectWorkflowMapping, Guid> mappings, WorkflowStore store)
    : IWorkflowRuntime, ITransientDependency
{
    public async Task<InitialState> GetInitialStateAsync(Guid projectId, Guid issueTypeId)
    {
        var mapping = await mappings.FirstOrDefaultAsync(item => item.ProjectId == projectId && item.IssueTypeId == issueTypeId);
        var workflowId = mapping?.WorkflowId ?? WorkflowsDataSeedContributor.DefaultWorkflowId;
        var version = await store.FindPublishedAsync(workflowId) ?? throw new BusinessException(WorkflowErrors.NotPublished);
        return new InitialState(version.InitialStatusId, version.Id);
    }
}

public record EvaluatedTransition(Transition Transition, List<TransitionRule> Rules);

[Authorize]
public class TransitionAppService(
    IRepository<Transition, Guid> transitions,
    IRepository<TransitionRule, Guid> rules,
    IWorkflowIssueGateway issues,
    IProjectAccess projectAccess,
    IStatusLookup statusLookup,
    ICurrentActor currentActor,
    RuleRegistry registry,
    IDistributedEventBus eventBus)
    : ApplicationService, ITransitionAppService
{
    public async Task<List<AvailableTransitionDto>> GetAvailableAsync(Guid issueId)
    {
        var subject = await issues.GetSubjectAsync(issueId);
        await projectAccess.CheckAsync(subject.ProjectId, ProjectPermissions.ViewIssues);
        if (!await projectAccess.IsGrantedAsync(subject.ProjectId, ProjectPermissions.TransitionIssues))
        {
            return [];
        }

        var statuses = await GetStatusesAsync();
        var result = new List<AvailableTransitionDto>();
        foreach (var item in await GetCandidatesAsync(subject))
        {
            var context = Context(subject, statuses, item.Transition.ToStatusId, comment: null);
            if (!await ConditionsMetAsync(item, context))
            {
                continue;
            }

            var problems = await ValidateAsync(item, context);
            result.Add(new AvailableTransitionDto
            {
                Id = item.Transition.Id,
                Name = item.Transition.Name,
                ToStatus = statuses[item.Transition.ToStatusId],
                RequiresComment = problems.Any(problem => problem.Code == WorkflowErrors.CommentRequired),
                MissingFields = [.. problems.Where(problem => problem.Field is not null).Select(problem => problem.Field!)],
                Problems = [.. problems.Where(problem => problem.Field is null && problem.Code != WorkflowErrors.CommentRequired).Select(problem => problem.Code)],
            });
        }

        return result;
    }

    public async Task ExecuteAsync(Guid issueId, ExecuteTransitionInput input)
    {
        var subject = await issues.GetSubjectAsync(issueId);
        await projectAccess.CheckAsync(subject.ProjectId, ProjectPermissions.TransitionIssues);

        if (subject.ConcurrencyStamp != input.ConcurrencyStamp)
        {
            throw new AbpDbConcurrencyException("The issue was changed by someone else.");
        }

        var candidates = await GetCandidatesAsync(subject);
        var item = candidates.FirstOrDefault(candidate => candidate.Transition.Id == input.TransitionId)
            ?? throw new BusinessException(WorkflowErrors.InvalidTransition);

        var statuses = await GetStatusesAsync();
        var toStatus = statuses[item.Transition.ToStatusId];
        if (!await ConditionsMetAsync(item, Context(subject, statuses, toStatus.Id, input.Comment)))
        {
            throw new AbpAuthorizationException(code: WorkflowErrors.ConditionFailed);
        }

        foreach (var (field, value) in input.Fields)
        {
            await issues.SetFieldAsync(issueId, field, value);
        }

        subject = await issues.GetSubjectAsync(issueId);
        var context = Context(subject, statuses, toStatus.Id, input.Comment);
        var problems = await ValidateAsync(item, context);
        if (problems.Count > 0)
        {
            throw new BusinessException(WorkflowErrors.ValidationFailed)
                .WithData("problems", string.Join(",", problems.Select(problem => problem.Field ?? problem.Code)));
        }

        await issues.MoveAsync(issueId, toStatus.Id, statuses[subject.StatusId].Name, toStatus.Name);

        if (!string.IsNullOrWhiteSpace(input.Comment))
        {
            await issues.AddCommentAsync(issueId, input.Comment);
        }

        foreach (var rule in item.Rules.Where(rule => rule.Kind == RuleKind.PostFunction))
        {
            await registry.PostFunction(rule.RuleKey).RunAsync(context, Config(rule));
        }

        await eventBus.PublishAsync(
            new IssueTransitionedEto
            {
                IssueId = issueId,
                ProjectId = subject.ProjectId,
                FromStatusId = subject.StatusId,
                ToStatusId = toStatus.Id,
            },
            useOutbox: true);
    }

    private async Task<Dictionary<Guid, StatusDto>> GetStatusesAsync() =>
        (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);

    private RuleContext Context(TransitionSubject subject, Dictionary<Guid, StatusDto> statuses, Guid toStatusId, string? comment) =>
        new(subject, currentActor.Actor, statuses, toStatusId, comment);

    private async Task<List<EvaluatedTransition>> GetCandidatesAsync(TransitionSubject subject)
    {
        if (subject.WorkflowVersionId is not { } versionId)
        {
            return [];
        }

        var list = await transitions.GetListAsync(transition => transition.VersionId == versionId);
        var matching = list
            .Where(transition => transition.FromStatusId == subject.StatusId
                || (transition.FromStatusId is null && transition.ToStatusId != subject.StatusId))
            .ToList();
        var ids = matching.Select(transition => transition.Id).ToList();
        var ruleList = await rules.GetListAsync(rule => ids.Contains(rule.TransitionId));
        return [.. matching.Select(transition => new EvaluatedTransition(
            transition, [.. ruleList.Where(rule => rule.TransitionId == transition.Id).OrderBy(rule => rule.Order)]))];
    }

    private async Task<bool> ConditionsMetAsync(EvaluatedTransition item, RuleContext context)
    {
        foreach (var rule in item.Rules.Where(rule => rule.Kind == RuleKind.Condition))
        {
            if (!await registry.Condition(rule.RuleKey).IsMetAsync(context, Config(rule)))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<List<RuleProblem>> ValidateAsync(EvaluatedTransition item, RuleContext context)
    {
        var problems = new List<RuleProblem>();
        foreach (var rule in item.Rules.Where(rule => rule.Kind == RuleKind.Validator))
        {
            problems.AddRange(await registry.Validator(rule.RuleKey).ValidateAsync(context, Config(rule)));
        }

        return problems;
    }

    private static JsonElement? Config(TransitionRule rule) =>
        rule.ConfigJson is null ? null : JsonDocument.Parse(rule.ConfigJson).RootElement;
}

public class WorkflowsDataSeedContributor(
    IRepository<Status, Guid> statuses,
    IRepository<Workflow, Guid> workflows,
    IRepository<WorkflowVersion, Guid> versions,
    WorkflowStore store,
    IWorkflowIssueGateway issues,
    IGuidGenerator guids) : IDataSeedContributor, ITransientDependency
{
    public static readonly Guid ToDoId = new("6f1b1a10-0000-4000-8000-000000000001");
    public static readonly Guid InProgressId = new("6f1b1a10-0000-4000-8000-000000000002");
    public static readonly Guid DoneId = new("6f1b1a10-0000-4000-8000-000000000003");
    public static readonly Guid DefaultWorkflowId = new("6f1b1a10-0000-4000-8000-000000000010");

    public async Task SeedAsync(DataSeedContext context)
    {
        if (!await statuses.AnyAsync())
        {
            await statuses.InsertManyAsync(
            [
                new Status(ToDoId, "To Do", StatusCategory.ToDo, 0),
                new Status(InProgressId, "In Progress", StatusCategory.InProgress, 1),
                new Status(DoneId, "Done", StatusCategory.Done, 2),
            ], autoSave: true);
        }

        if (await workflows.AnyAsync(workflow => workflow.Id == DefaultWorkflowId))
        {
            return;
        }

        await workflows.InsertAsync(new Workflow(DefaultWorkflowId, "Default", "To Do, In Progress, Done"), autoSave: true);
        var version = await versions.InsertAsync(new WorkflowVersion(guids.Create(), DefaultWorkflowId, 1, ToDoId, null), autoSave: true);
        await store.WriteAsync(version, new WorkflowDefinitionDto
        {
            InitialStatusId = ToDoId,
            StatusIds = [ToDoId, InProgressId, DoneId],
            Transitions =
            [
                new TransitionDefinitionDto { Name = "To Do", ToStatusId = ToDoId },
                new TransitionDefinitionDto { Name = "In Progress", ToStatusId = InProgressId },
                new TransitionDefinitionDto { Name = "Done", ToStatusId = DoneId },
            ],
        });
        version.State = WorkflowVersionState.Published;
        await versions.UpdateAsync(version, autoSave: true);

        // Issues created before workflows existed join the default workflow.
        await issues.MigrateBatchAsync(new IssueScope([]), version.Id, new Dictionary<Guid, Guid>(), int.MaxValue);
    }
}

public static class WorkflowsModelBuilderExtensions
{
    public const string Schema = "workflows";

    public static void ConfigureWorkflows(this ModelBuilder builder)
    {
        builder.Entity<Status>(b =>
        {
            b.ToTable("statuses", Schema);
            b.Property(status => status.Name).HasMaxLength(64).IsRequired();
        });
        builder.Entity<Workflow>(b =>
        {
            b.ToTable("workflows", Schema);
            b.Property(workflow => workflow.Name).HasMaxLength(128).IsRequired();
            b.Property(workflow => workflow.Description).HasMaxLength(512);
        });
        builder.Entity<WorkflowVersion>(b =>
        {
            b.ToTable("workflow_versions", Schema);
            b.HasIndex(version => new { version.WorkflowId, version.VersionNumber }).IsUnique();
        });
        builder.Entity<WorkflowStep>(b =>
        {
            b.ToTable("workflow_steps", Schema);
            b.HasIndex(step => new { step.VersionId, step.StatusId }).IsUnique();
        });
        builder.Entity<Transition>(b =>
        {
            b.ToTable("transitions", Schema);
            b.Property(transition => transition.Name).HasMaxLength(128).IsRequired();
            b.HasIndex(transition => transition.VersionId);
        });
        builder.Entity<TransitionRule>(b =>
        {
            b.ToTable("transition_rules", Schema);
            b.Property(rule => rule.RuleKey).HasMaxLength(64).IsRequired();
            b.HasIndex(rule => rule.TransitionId);
        });
        builder.Entity<ProjectWorkflowMapping>(b =>
        {
            b.ToTable("project_workflow_mappings", Schema);
            b.HasIndex(mapping => new { mapping.ProjectId, mapping.IssueTypeId }).IsUnique();
        });
        builder.Entity<WorkflowMigration>(b =>
        {
            b.ToTable("workflow_migrations", Schema);
            b.HasIndex(migration => migration.State);
        });
        builder.Entity<WebhookDelivery>(b =>
        {
            b.ToTable("webhook_deliveries", Schema);
            b.Property(delivery => delivery.Url).HasMaxLength(2048).IsRequired();
            b.HasIndex(delivery => new { delivery.State, delivery.NextAttemptAt });
        });
    }
}
