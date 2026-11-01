using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;

namespace SprintBoot.Projects;

public class ProjectMembership(
    IRepository<Project, Guid> projects,
    IRepository<ProjectMember, Guid> members,
    IDistributedEventBus eventBus,
    IGuidGenerator guids) : IProjectMembership, ITransientDependency
{
    public async Task AddAsync(Guid projectId, Guid userId, string role, Guid actorId)
    {
        if (await projects.FindAsync(projectId) is null
            || await members.AnyAsync(member => member.ProjectId == projectId && member.UserId == userId))
        {
            return;
        }

        await members.InsertAsync(new ProjectMember(guids.Create(), projectId, userId, role), autoSave: true);
        await eventBus.PublishAsync(
            new ProjectMemberAddedEto { EventId = guids.Create(), ProjectId = projectId, UserId = userId, ActorId = actorId },
            useOutbox: true);
    }
}
