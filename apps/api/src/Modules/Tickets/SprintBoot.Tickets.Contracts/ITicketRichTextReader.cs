namespace SprintBoot.Tickets;

/// <summary>The rich text of a ticket, for modules that read links out of it with no calling user.</summary>
public interface ITicketRichTextReader
{
    /// <summary>The description and every comment, as sanitized HTML; empty when the ticket is gone.</summary>
    Task<IReadOnlyList<string>> GetAsync(Guid ticketId);
}
