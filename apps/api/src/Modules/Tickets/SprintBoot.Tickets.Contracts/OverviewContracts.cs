using SprintBoot.Identity;
using SprintBoot.Workflows;
using Volo.Abp.Application.Services;

namespace SprintBoot.Tickets;

/// <summary>One ticket as the space overview counts it.</summary>
/// <param name="CompletedAt">When a done ticket last changed status, so roughly when it was finished; null while it is open.</param>
public record TicketOverviewRow(
    Guid Id,
    Guid? ParentId,
    string TypeName,
    string TypeIcon,
    TicketHierarchy Hierarchy,
    StatusCategory Category,
    string StatusName,
    Guid? AssigneeId,
    decimal? StoryPoints,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt);

public enum TicketEventKind
{
    Created = 0,
    Changed = 1,
    Commented = 2,
}

/// <summary>Something a person did to a ticket of the space.</summary>
/// <param name="Field">The field a change touched, as the ticket history names it.</param>
/// <param name="NewValue">Kept only for a status change (the status name) and for logged work (minutes).</param>
public record TicketEventRow(Guid ActorId, TicketEventKind Kind, string TicketKey, string TicketTitle, string? Field, string? NewValue, DateTime At);

/// <summary>What the space overview needs from tickets, without touching their tables.</summary>
public interface ITicketOverviewFacts
{
    /// <summary>Every ticket of the space, epics and subtasks included.</summary>
    Task<List<TicketOverviewRow>> GetTicketsAsync(Guid projectId);

    /// <summary>The latest creations, changes and comments, newest first.</summary>
    Task<List<TicketEventRow>> GetRecentEventsAsync(Guid projectId, int take);
}

public class ProjectIntroductionDto
{
    /// <summary>Sanitized rich text HTML; empty until someone writes it.</summary>
    public required string Content { get; set; }

    public required UserRefDto? UpdatedBy { get; set; }

    public required DateTime? UpdatedAt { get; set; }

    /// <summary>Whether the reader may change it.</summary>
    public required bool CanEdit { get; set; }
}

public class UpdateProjectIntroductionInput
{
    public string? Content { get; set; }
}

/// <summary>The page that introduces a space. Readers of its tickets read it; its admins write it.</summary>
public interface IProjectIntroductionAppService : IApplicationService
{
    Task<ProjectIntroductionDto> GetAsync(Guid projectId);

    Task<ProjectIntroductionDto> UpdateAsync(Guid projectId, UpdateProjectIntroductionInput input);
}
