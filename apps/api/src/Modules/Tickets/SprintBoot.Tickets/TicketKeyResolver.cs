using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Tickets;

public class TicketKeyResolver(IRepository<Ticket, Guid> tickets, IProjectAccess projects) : ITicketKeyResolver, ITransientDependency
{
    public async Task<Dictionary<string, TicketRef>> ResolveAsync(IReadOnlyCollection<string> keys)
    {
        var result = new Dictionary<string, TicketRef>();
        foreach (var key in keys)
        {
            var separator = key.LastIndexOf('-');
            if (separator <= 0 || !int.TryParse(key[(separator + 1)..], out var number))
            {
                continue;
            }

            var project = await projects.FindRefByKeyAsync(key[..separator]);
            var ticket = project is null ? null : await tickets.FirstOrDefaultAsync(item => item.ProjectId == project.Id && item.Number == number);
            if (ticket is not null)
            {
                result[key] = new TicketRef(ticket.Id, ticket.ProjectId);
            }
        }

        return result;
    }
}
