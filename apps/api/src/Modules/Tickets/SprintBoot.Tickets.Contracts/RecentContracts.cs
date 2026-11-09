using Volo.Abp.Application.Services;

namespace SprintBoot.Tickets;

/// <summary>The tickets and epics a person opened lately, for getting back to them quickly.</summary>
public interface IRecentTicketAppService : IApplicationService
{
    /// <summary>Notes that the caller opened the ticket; opening it again only moves it to the top. Needs the right to view it.</summary>
    Task VisitAsync(Guid ticketId);

    /// <summary>The caller's latest ones, newest first, leaving out what they can no longer open.</summary>
    Task<List<TicketSummaryDto>> GetListAsync();
}
