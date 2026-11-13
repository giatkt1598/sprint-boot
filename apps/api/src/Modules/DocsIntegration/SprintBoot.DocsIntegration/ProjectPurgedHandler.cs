using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.DocsIntegration;

/// <summary>A purged space forgets its Docmost space and its tickets' page links; the pages stay in Docmost.</summary>
public class ProjectPurgedHandler(
    IRepository<ProjectDocsSpace, Guid> spaces,
    IRepository<TicketDocLink, Guid> links)
    : IDistributedEventHandler<ProjectPurgedEto>, ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task HandleEventAsync(ProjectPurgedEto eventData)
    {
        await links.DeleteDirectAsync(item => item.ProjectId == eventData.ProjectId);
        await spaces.DeleteDirectAsync(item => item.ProjectId == eventData.ProjectId);
    }
}
