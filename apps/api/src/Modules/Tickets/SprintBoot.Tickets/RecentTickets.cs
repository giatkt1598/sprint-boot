using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Projects;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace SprintBoot.Tickets;

/// <summary>One person's last visit to one ticket. There is a row per pair, moved to the top by each new visit.</summary>
public class RecentTicket : Entity<Guid>
{
    public Guid UserId { get; private set; }

    public Guid TicketId { get; private set; }

    public DateTime ViewedAt { get; set; }

    protected RecentTicket()
    {
    }

    public RecentTicket(Guid id, Guid userId, Guid ticketId, DateTime viewedAt)
        : base(id)
    {
        UserId = userId;
        TicketId = ticketId;
        ViewedAt = viewedAt;
    }
}

[Authorize]
public class RecentTicketAppService(
    IRepository<RecentTicket, Guid> recents,
    IRepository<Ticket, Guid> tickets,
    IProjectAccess projectAccess,
    TicketMapper mapper,
    IClock clock)
    : ApplicationService, IRecentTicketAppService
{
    /// <summary>What the list shows.</summary>
    private const int ListSize = 20;

    /// <summary>What is kept per person; the extra covers tickets that drop out of the list because access ended.</summary>
    private const int KeptPerUser = 50;

    public async Task VisitAsync(Guid ticketId)
    {
        var ticket = await tickets.FindAsync(ticketId) ?? throw new EntityNotFoundException(typeof(Ticket), ticketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);

        var userId = CurrentUser.GetId();
        var row = await recents.FirstOrDefaultAsync(item => item.UserId == userId && item.TicketId == ticketId);
        if (row is null)
        {
            await recents.InsertAsync(new RecentTicket(GuidGenerator.Create(), userId, ticketId, clock.Now), autoSave: true);
        }
        else
        {
            row.ViewedAt = clock.Now;
            await recents.UpdateAsync(row, autoSave: true);
        }

        var surplus = (await recents.GetListAsync(item => item.UserId == userId))
            .OrderByDescending(item => item.ViewedAt)
            .Skip(KeptPerUser)
            .ToList();
        if (surplus.Count > 0)
        {
            await recents.DeleteManyAsync(surplus, autoSave: true);
        }
    }

    public async Task<List<TicketSummaryDto>> GetListAsync()
    {
        var userId = CurrentUser.GetId();
        var rows = (await recents.GetListAsync(item => item.UserId == userId))
            .OrderByDescending(item => item.ViewedAt)
            .Take(KeptPerUser)
            .ToList();
        var ids = rows.Select(row => row.TicketId).ToList();

        // Spaces the person has left, and tickets deleted since, simply drop out.
        var visible = await projectAccess.GetVisibleProjectIdsAsync();
        var query = (await tickets.GetQueryableAsync()).Where(ticket => ids.Contains(ticket.Id));
        if (visible is not null)
        {
            query = query.Where(ticket => visible.Contains(ticket.ProjectId));
        }

        var found = await query.ToListAsync();
        var summaries = (await mapper.ToSummariesAsync(found)).ToDictionary(summary => summary.Id);
        return [.. rows.Where(row => summaries.ContainsKey(row.TicketId)).Select(row => summaries[row.TicketId]).Take(ListSize)];
    }
}
