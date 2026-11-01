using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Local;

namespace SprintBoot.Workflows;

/// <summary>
/// Takes a status out of the system. Published versions and issues cannot lose a status,
/// so while they use it a replacement takes its place everywhere.
/// </summary>
public class StatusRemover(
    IRepository<Status, Guid> statuses,
    IRepository<WorkflowVersion, Guid> versions,
    IRepository<WorkflowStep, Guid> steps,
    IRepository<Transition, Guid> transitions,
    IRepository<TransitionRule, Guid> rules,
    IRepository<WorkflowMigration, Guid> migrations,
    IWorkflowIssueGateway issues,
    ILocalEventBus localEventBus)
    : ITransientDependency
{
    public async Task<StatusUsageDto> GetUsageAsync(Guid id)
    {
        var issueCount = await issues.CountWithStatusAsync(id);
        var workflowCount = (await PublishedVersionsUsingAsync(id)).Select(version => version.WorkflowId).Distinct().Count();
        return new StatusUsageDto
        {
            IssueCount = issueCount,
            WorkflowCount = workflowCount,
            ReplacementRequired = issueCount > 0 || workflowCount > 0,
        };
    }

    public async Task RemoveAsync(Status status, Guid? replacementId)
    {
        // A queued migration may still move issues onto this status.
        if (await migrations.AnyAsync(migration => migration.State == MigrationState.Pending))
        {
            throw new BusinessException(WorkflowErrors.MigrationRunning);
        }

        if (replacementId is { } target)
        {
            await CheckReplacementAsync(status, target);
        }
        else if ((await GetUsageAsync(status.Id)).ReplacementRequired)
        {
            throw new BusinessException(WorkflowErrors.StatusInUse);
        }

        await RewriteVersionsAsync(status.Id, replacementId);
        if (replacementId is { } replacement)
        {
            await issues.ReplaceStatusAsync(status.Id, replacement);
        }

        await localEventBus.PublishAsync(new StatusRemovedEvent(status.Id, replacementId), onUnitOfWorkComplete: false);
        await statuses.DeleteAsync(status, autoSave: true);
    }

    private async Task CheckReplacementAsync(Status status, Guid replacementId)
    {
        var replacement = await statuses.GetAsync(replacementId);
        if (replacement.Id == status.Id)
        {
            throw new BusinessException(WorkflowErrors.InvalidDefinition);
        }

        // A global status reaches every space, so a space's own status cannot stand in for it.
        if (replacement.ProjectId is not null && replacement.ProjectId != status.ProjectId)
        {
            throw new BusinessException(WorkflowErrors.OutOfScope);
        }

        if (status.Category != StatusCategory.Done || replacement.Category == StatusCategory.Done)
        {
            return;
        }

        var doneIds = (await statuses.GetListAsync(item => item.Category == StatusCategory.Done && item.Id != status.Id))
            .Select(item => item.Id)
            .ToList();
        foreach (var version in await PublishedVersionsUsingAsync(status.Id))
        {
            if (!await steps.AnyAsync(step => step.VersionId == version.Id && doneIds.Contains(step.StatusId)))
            {
                throw new BusinessException(WorkflowErrors.NoDoneStatus);
            }
        }
    }

    private async Task<List<WorkflowVersion>> PublishedVersionsUsingAsync(Guid statusId)
    {
        var used = (await steps.GetListAsync(step => step.StatusId == statusId)).Select(step => step.VersionId).ToList();
        return await versions.GetListAsync(version => version.State == WorkflowVersionState.Published && used.Contains(version.Id));
    }

    /// <summary>Archived versions change too, so none keeps pointing at a status that is gone.</summary>
    private async Task RewriteVersionsAsync(Guid statusId, Guid? replacementId)
    {
        var touched = await transitions.GetListAsync(item => item.FromStatusId == statusId || item.ToStatusId == statusId);
        var held = await steps.GetListAsync(step => step.StatusId == statusId);
        var versionIds = held.Select(step => step.VersionId).Concat(touched.Select(item => item.VersionId)).Distinct().ToList();

        foreach (var version in await versions.GetListAsync(version => versionIds.Contains(version.Id)))
        {
            var siblings = await steps.GetListAsync(step => step.VersionId == version.Id);
            await steps.DeleteManyAsync(siblings.Where(step => step.StatusId == statusId), autoSave: true);

            if (replacementId is { } replacement && siblings.All(step => step.StatusId != replacement))
            {
                await steps.InsertAsync(new WorkflowStep(Guid.NewGuid(), version.Id, replacement), autoSave: true);
                version.LayoutJson = version.LayoutJson?.Replace(statusId.ToString(), replacement.ToString());
            }

            if (version.InitialStatusId == statusId)
            {
                version.InitialStatusId = replacementId
                    ?? siblings.FirstOrDefault(step => step.StatusId != statusId)?.StatusId
                    ?? throw new BusinessException(WorkflowErrors.StatusInUse);
            }

            await versions.UpdateAsync(version, autoSave: true);
        }

        foreach (var transition in touched)
        {
            if (replacementId is { } replacement)
            {
                transition.ReplaceStatus(statusId, replacement);
            }

            if (replacementId is null || transition.FromStatusId == transition.ToStatusId)
            {
                await rules.DeleteAsync(rule => rule.TransitionId == transition.Id, autoSave: true);
                await transitions.DeleteAsync(transition, autoSave: true);
            }
            else
            {
                await transitions.UpdateAsync(transition, autoSave: true);
            }
        }
    }
}
