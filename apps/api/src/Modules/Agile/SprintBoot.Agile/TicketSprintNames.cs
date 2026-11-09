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

    public async Task<Dictionary<Guid, int>> GetRanksAsync(IReadOnlyCollection<Guid> ticketIds)
    {
        var ids = ticketIds.ToList();
        var rows = await sprintTickets.GetListAsync(item => ids.Contains(item.TicketId) && item.RemovedAt == null);
        var sprintIds = rows.Select(row => row.SprintId).Distinct().ToList();
        var known = await sprints.GetListAsync(item => sprintIds.Contains(item.Id));
        // Closed sprints first, oldest first; then the running one; then planned ones in the order they were made.
        var order = known
            .OrderBy(item => item.State switch { SprintState.Closed => 0, SprintState.Active => 1, _ => 2 })
            .ThenBy(item => item.StartDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.CreationTime)
            .Select((item, index) => (item.Id, index))
            .ToDictionary(item => item.Id, item => item.index);

        return rows
            .GroupBy(row => row.TicketId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => known.First(item => item.Id == row.SprintId))
                    .OrderBy(item => item.State == SprintState.Closed)
                    .ThenByDescending(item => item.CreationTime)
                    .Select(item => order[item.Id])
                    .First());
    }

    public async Task<HashSet<Guid>> GetTicketIdsAsync(IReadOnlyCollection<Guid> sprintIds)
    {
        var ids = sprintIds.ToList();
        var rows = await sprintTickets.GetListAsync(item => ids.Contains(item.SprintId) && (item.RemovedAt == null || item.Outcome != SprintOutcome.Open));
        return [.. rows.Select(row => row.TicketId)];
    }
}
