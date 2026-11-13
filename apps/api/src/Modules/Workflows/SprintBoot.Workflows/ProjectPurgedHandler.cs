using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.Workflows;

/// <summary>A purged space's own workflows and statuses go with it; shared ones only lose its mappings.</summary>
public class ProjectPurgedHandler(
    IRepository<Status, Guid> statuses,
    IRepository<Workflow, Guid> workflows,
    IRepository<WorkflowVersion, Guid> versions,
    IRepository<WorkflowStep, Guid> steps,
    IRepository<Transition, Guid> transitions,
    IRepository<TransitionRule, Guid> rules,
    IRepository<ProjectWorkflowMapping, Guid> mappings)
    : IDistributedEventHandler<ProjectPurgedEto>, ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task HandleEventAsync(ProjectPurgedEto eventData)
    {
        Guid? projectId = eventData.ProjectId;
        var workflowIds = (await workflows.GetQueryableAsync()).Where(item => item.ProjectId == projectId).Select(item => item.Id);
        var versionIds = (await versions.GetQueryableAsync()).Where(item => workflowIds.Contains(item.WorkflowId)).Select(item => item.Id);
        var transitionIds = (await transitions.GetQueryableAsync()).Where(item => versionIds.Contains(item.VersionId)).Select(item => item.Id);

        await mappings.DeleteDirectAsync(item => item.ProjectId == eventData.ProjectId);
        await rules.DeleteDirectAsync(item => transitionIds.Contains(item.TransitionId));
        await transitions.DeleteDirectAsync(item => versionIds.Contains(item.VersionId));
        await steps.DeleteDirectAsync(item => versionIds.Contains(item.VersionId));
        await versions.DeleteDirectAsync(item => workflowIds.Contains(item.WorkflowId));
        await workflows.DeleteDirectAsync(item => item.ProjectId == projectId);
        await statuses.DeleteDirectAsync(item => item.ProjectId == projectId);
    }
}
