using SprintBoot.Tickets;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Agile;

/// <summary>An open sprint wins over a closed one, then the newest, as on the ticket page.</summary>
public class TicketSprintNames(
    IRepository<SprintTicket, Guid> sprintTickets,
    IRepository<Sprint, Guid> sprints) : ITicketSprintNames, ITransientDependency
{
    public async Task<Dictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ticketIds)
    {
        var ids = ticketIds.ToList();
        var rows = await sprintTickets.GetListAsync(item => ids.Contains(item.TicketId) && item.RemovedAt == null);
        var sprintIds = rows.Select(row => row.SprintId).Distinct().ToList();
        var known = (await sprints.GetListAsync(item => sprintIds.Contains(item.Id))).ToDictionary(item => item.Id);

        return rows
            .GroupBy(row => row.TicketId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => known[row.SprintId])
                    .OrderBy(item => item.State == SprintState.Closed)
                    .ThenByDescending(item => item.CreationTime)
                    .First().Name);
    }
}
