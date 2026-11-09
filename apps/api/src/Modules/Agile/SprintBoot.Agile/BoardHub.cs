using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SprintBoot.Tickets;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace SprintBoot.Agile;

[Authorize]
public class BoardHub(IProjectAccess projectAccess, ICurrentPrincipalAccessor principal, IUnitOfWorkManager unitOfWork) : Hub
{
    public const string Path = "/hubs/board";
    public const string ChangedEvent = "BoardChanged";

    public static string Group(Guid projectId) => $"project:{projectId}";

    public async Task Join(Guid projectId)
    {
        // Throws for outsiders, so nobody can listen to a project they cannot view.
        using (principal.Change(Context.User!))
        using (var uow = unitOfWork.Begin(requiresNew: true))
        {
            await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
            await uow.CompleteAsync();
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(projectId));
    }

    public Task Leave(Guid projectId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(projectId));
}

public class BoardNotifier(IHubContext<BoardHub> hub) : ITransientDependency
{
    /// <summary>Clients refetch on this signal; the server stays the single source of truth.</summary>
    public Task ChangedAsync(Guid projectId) =>
        hub.Clients.Group(BoardHub.Group(projectId)).SendAsync(BoardHub.ChangedEvent, projectId);
}

public class BoardChangeHandler(BoardNotifier notifier) :
    IDistributedEventHandler<TicketCreatedEto>,
    IDistributedEventHandler<TicketUpdatedEto>,
    IDistributedEventHandler<TicketTransitionedEto>,
    ITransientDependency
{
    public Task HandleEventAsync(TicketCreatedEto eventData) => notifier.ChangedAsync(eventData.ProjectId);

    public Task HandleEventAsync(TicketUpdatedEto eventData) => notifier.ChangedAsync(eventData.ProjectId);

    public Task HandleEventAsync(TicketTransitionedEto eventData) => notifier.ChangedAsync(eventData.ProjectId);
}
