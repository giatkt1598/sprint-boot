using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.Notifications;

/// <summary>Notifications about a purged space point at nothing, so they go with it, as does its scheme choice.</summary>
public class ProjectPurgedHandler(
    IRepository<Notification, Guid> notifications,
    IRepository<ProjectNotificationScheme, Guid> projectSchemes)
    : IDistributedEventHandler<ProjectPurgedEto>, ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task HandleEventAsync(ProjectPurgedEto eventData)
    {
        await notifications.DeleteDirectAsync(item => item.ProjectId == eventData.ProjectId);
        await projectSchemes.DeleteDirectAsync(item => item.ProjectId == eventData.ProjectId);
    }
}
