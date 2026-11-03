using SprintBoot.Identity;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.Projects;

/// <summary>A deleted person leaves every space, even as its last admin: system admins still manage it.</summary>
public class UserDeletedHandler(IRepository<ProjectMember, Guid> members) : IDistributedEventHandler<UserDeletedEto>, ITransientDependency
{
    [UnitOfWork]
    public virtual async Task HandleEventAsync(UserDeletedEto eventData) =>
        await members.DeleteAsync(member => member.UserId == eventData.UserId, autoSave: true);
}
