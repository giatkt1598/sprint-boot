using SprintBoot.Identity;
using SprintBoot.Workflows;

namespace SprintBoot.Tickets;

public class TicketCardDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required TicketTypeDto Type { get; set; }

    public required StatusDto Status { get; set; }

    public required PriorityDto Priority { get; set; }

    public required UserRefDto? Assignee { get; set; }

    public required UserRefDto? Reporter { get; set; }

    public required string[] Labels { get; set; }

    public required Guid? EpicId { get; set; }

    public required string? EpicTitle { get; set; }

    public required decimal? StoryPoints { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required DateTime UpdatedAt { get; set; }

    public required string ConcurrencyStamp { get; set; }
}

public record TicketFacts(Guid Id, string Key, string Title, Guid? AssigneeId, Guid? ReporterId, IReadOnlyCollection<Guid> Watchers);

/// <summary>What background consumers need to know about a ticket. Null when it is gone.</summary>
public interface ITicketFactsProvider
{
    Task<TicketFacts?> FindAsync(Guid ticketId);
}

/// <summary>Sprints belong to Agile, which depends on Tickets; lists read their names through this.</summary>
public interface ITicketSprintNames
{
    /// <summary>The current sprint of each ticket; tickets in no sprint are left out.</summary>
    Task<Dictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ticketIds);
}

public record TicketRef(Guid TicketId, Guid ProjectId);

/// <summary>For background work with no calling user. Keys that match no ticket are left out.</summary>
public interface ITicketKeyResolver
{
    Task<Dictionary<string, TicketRef>> ResolveAsync(IReadOnlyCollection<string> keys);
}

public class WatchersDto
{
    public required int Count { get; set; }

    public required bool WatchedByMe { get; set; }

    public required List<UserRefDto> Users { get; set; }
}

public class EpicRowDto
{
    public required Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Title { get; set; }

    public required StatusDto Status { get; set; }

    public required PriorityDto Priority { get; set; }

    public required DateOnly? DueDate { get; set; }

    public required UserRefDto? Lead { get; set; }

    public required string ConcurrencyStamp { get; set; }

    public required int Total { get; set; }

    public required int Done { get; set; }

    public required int InProgress { get; set; }
}

/// <summary>Ticket data for backlog and board. Results keep rank order; subtasks and epics are never rows.</summary>
public interface ITicketPlanningGateway
{
    Task<List<TicketCardDto>> GetCardsAsync(Guid projectId, IReadOnlyCollection<Guid> ticketIds);

    /// <summary>Open tickets outside the excluded set, for the backlog.</summary>
    Task<List<TicketCardDto>> GetBacklogCardsAsync(Guid projectId, IReadOnlyCollection<Guid> excludedTicketIds);

    Task<TicketCardDto?> FindCardAsync(Guid ticketId);

    Task<Guid> GetProjectIdAsync(Guid ticketId);

    /// <summary>Writes only this ticket's rank, between its new neighbours.</summary>
    Task MoveRankAsync(Guid ticketId, Guid? afterTicketId, Guid? beforeTicketId);

    Task RecordActivityAsync(Guid ticketId, string field, string? oldValue, string? newValue);
}
