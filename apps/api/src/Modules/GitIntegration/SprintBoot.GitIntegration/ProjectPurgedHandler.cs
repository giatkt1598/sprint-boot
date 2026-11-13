using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.GitIntegration;

/// <summary>
/// A purged space's git connections go with everything read through them. Webhooks registered on the provider
/// stay there; their deliveries now find no connection and are dropped.
/// </summary>
public class ProjectPurgedHandler(
    IRepository<GitConnection, Guid> connections,
    IRepository<GitRepository, Guid> repositories,
    IRepository<GitBranch, Guid> branches,
    IRepository<GitCommit, Guid> commits,
    IRepository<GitPullRequest, Guid> pullRequests,
    IRepository<CiStatus, Guid> ciStatuses,
    IRepository<TicketGitLink, Guid> links,
    IRepository<GitWebhookDelivery, Guid> deliveries)
    : IDistributedEventHandler<ProjectPurgedEto>, ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task HandleEventAsync(ProjectPurgedEto eventData)
    {
        Guid? projectId = eventData.ProjectId;
        var connectionIds = (await connections.GetQueryableAsync()).Where(item => item.ProjectId == projectId).Select(item => item.Id);
        var repositoryIds = (await repositories.GetQueryableAsync()).Where(item => connectionIds.Contains(item.ConnectionId)).Select(item => item.Id);

        await links.DeleteDirectAsync(item => item.ProjectId == eventData.ProjectId);
        await ciStatuses.DeleteDirectAsync(item => repositoryIds.Contains(item.RepositoryId));
        await pullRequests.DeleteDirectAsync(item => repositoryIds.Contains(item.RepositoryId));
        await commits.DeleteDirectAsync(item => repositoryIds.Contains(item.RepositoryId));
        await branches.DeleteDirectAsync(item => repositoryIds.Contains(item.RepositoryId));
        await repositories.DeleteDirectAsync(item => connectionIds.Contains(item.ConnectionId));
        await deliveries.DeleteDirectAsync(item => item.ConnectionId != null && connectionIds.Contains(item.ConnectionId.Value));
        await connections.DeleteDirectAsync(item => item.ProjectId == projectId);
    }
}
