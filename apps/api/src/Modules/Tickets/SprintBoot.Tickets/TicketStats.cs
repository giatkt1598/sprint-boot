using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Tickets;

public class TicketProjectStats(IRepository<Ticket, Guid> tickets) : ITicketProjectStats, ITransientDependency
{
    public async Task<Dictionary<Guid, ProjectTicketStats>> GetAsync(IEnumerable<Guid> projectIds)
    {
        var ids = projectIds.ToList();
        var query = await tickets.GetQueryableAsync();
        var rows = await query.Where(ticket => ids.Contains(ticket.ProjectId))
            .GroupBy(ticket => ticket.ProjectId)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                UpdatedAt = group.Max(ticket => ticket.LastModificationTime ?? ticket.CreationTime),
            })
            .ToListAsync();
        return rows.ToDictionary(row => row.Key, row => new ProjectTicketStats(row.Count, row.UpdatedAt));
    }
}
