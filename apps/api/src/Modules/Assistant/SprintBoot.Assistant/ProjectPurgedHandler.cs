using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.Assistant;

/// <summary>Conversations held about a purged space go with it.</summary>
public class ProjectPurgedHandler(
    IRepository<Conversation, Guid> conversations,
    IRepository<ConversationMessage, Guid> messages,
    IRepository<AssistantAction, Guid> actions)
    : IDistributedEventHandler<ProjectPurgedEto>, ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task HandleEventAsync(ProjectPurgedEto eventData)
    {
        var ids = (await conversations.GetQueryableAsync()).Where(item => item.ProjectId == eventData.ProjectId).Select(item => item.Id);
        await actions.DeleteDirectAsync(item => ids.Contains(item.ConversationId));
        await messages.DeleteDirectAsync(item => ids.Contains(item.ConversationId));
        await conversations.DeleteDirectAsync(item => item.ProjectId == eventData.ProjectId);
    }
}
