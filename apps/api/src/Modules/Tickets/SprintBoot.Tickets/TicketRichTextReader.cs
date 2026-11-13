using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Tickets;

public class TicketRichTextReader(IRepository<Ticket, Guid> tickets, IRepository<Comment, Guid> comments)
    : ITicketRichTextReader, ITransientDependency
{
    public async Task<IReadOnlyList<string>> GetAsync(Guid ticketId)
    {
        var ticket = await tickets.FindAsync(ticketId);
        if (ticket is null)
        {
            return [];
        }

        var bodies = (await comments.GetListAsync(comment => comment.TicketId == ticketId)).Select(comment => comment.Body);
        return [ticket.Description ?? string.Empty, .. bodies];
    }
}
