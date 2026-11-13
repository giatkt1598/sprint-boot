using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.Identity;

/// <summary>Pending invitations stop offering a purged space; the rest of each invitation stands.</summary>
public class ProjectPurgedHandler(IRepository<InvitationProject, Guid> invitationProjects)
    : IDistributedEventHandler<ProjectPurgedEto>, ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task HandleEventAsync(ProjectPurgedEto eventData) =>
        await invitationProjects.DeleteDirectAsync(item => item.ProjectId == eventData.ProjectId);
}
