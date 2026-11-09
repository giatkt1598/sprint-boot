using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Users;

namespace SprintBoot.Tickets;

public class WatcherStore(IRepository<TicketWatcher, Guid> watchers, IGuidGenerator guids) : ITransientDependency
{
    public async Task EnsureAsync(Guid ticketId, params Guid?[] userIds)
    {
        foreach (var userId in userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct())
        {
            if (!await watchers.AnyAsync(item => item.TicketId == ticketId && item.UserId == userId))
            {
                await watchers.InsertAsync(new TicketWatcher(guids.Create(), ticketId, userId), autoSave: true);
            }
        }
    }
}

[Authorize]
public class WatcherAppService(
    IRepository<Ticket, Guid> tickets,
    IRepository<TicketWatcher, Guid> watchers,
    WatcherStore store,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory)
    : ApplicationService, IWatcherAppService
{
    public async Task<WatchersDto> GetAsync(Guid ticketId)
    {
        await CheckAsync(ticketId);
        return await ToDtoAsync(ticketId);
    }

    public async Task<WatchersDto> WatchAsync(Guid ticketId)
    {
        await CheckAsync(ticketId);
        await store.EnsureAsync(ticketId, CurrentUser.GetId());
        return await ToDtoAsync(ticketId);
    }

    public async Task<WatchersDto> UnwatchAsync(Guid ticketId)
    {
        await CheckAsync(ticketId);
        var userId = CurrentUser.GetId();
        await watchers.DeleteAsync(item => item.TicketId == ticketId && item.UserId == userId, autoSave: true);
        return await ToDtoAsync(ticketId);
    }

    private async Task CheckAsync(Guid ticketId)
    {
        var ticket = await tickets.GetAsync(ticketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);
    }

    private async Task<WatchersDto> ToDtoAsync(Guid ticketId)
    {
        var ids = (await watchers.GetListAsync(item => item.TicketId == ticketId)).Select(item => item.UserId).ToList();
        var users = await userDirectory.GetByIdsAsync(ids);
        return new WatchersDto
        {
            Count = ids.Count,
            WatchedByMe = ids.Contains(CurrentUser.GetId()),
            Users = [.. users.Values.OrderBy(user => user.Name)],
        };
    }
}

public class TicketFactsProvider(IRepository<Ticket, Guid> tickets, IRepository<TicketWatcher, Guid> watchers, IProjectAccess projectAccess)
    : ITicketFactsProvider, ITransientDependency
{
    public async Task<TicketFacts?> FindAsync(Guid ticketId)
    {
        var ticket = await tickets.FindAsync(ticketId);
        if (ticket is null)
        {
            return null;
        }

        var project = await projectAccess.GetRefAsync(ticket.ProjectId);
        var watching = await watchers.GetListAsync(item => item.TicketId == ticketId);
        return new TicketFacts(
            ticket.Id, $"{project.Key}-{ticket.Number}", ticket.Title, ticket.AssigneeId, ticket.ReporterId,
            [.. watching.Select(item => item.UserId)]);
    }
}
