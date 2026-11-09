using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Services;

namespace SprintBoot.Tickets;

/// <summary>A kind of relationship between two tickets, read one way from each side: "blocks" and "is blocked by".</summary>
public class TicketLinkTypeDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>How the source ticket reads: "blocks".</summary>
    public required string Outward { get; set; }

    /// <summary>How the target ticket reads: "is blocked by".</summary>
    public required string Inward { get; set; }

    public required int LinkCount { get; set; }
}

public class SaveTicketLinkTypeInput
{
    [Required]
    [StringLength(TicketLinkConsts.MaxNameLength)]
    public required string Name { get; set; }

    [Required]
    [StringLength(TicketLinkConsts.MaxNameLength)]
    public required string Outward { get; set; }

    [Required]
    [StringLength(TicketLinkConsts.MaxNameLength)]
    public required string Inward { get; set; }
}

public enum TicketLinkDirection
{
    /// <summary>This ticket is the source: it "blocks" the other.</summary>
    Outward = 0,

    /// <summary>This ticket is the target: it "is blocked by" the other.</summary>
    Inward = 1,
}

/// <summary>One link as seen from one ticket.</summary>
public class TicketLinkDto
{
    public required Guid Id { get; set; }

    public required Guid LinkTypeId { get; set; }

    public required TicketLinkDirection Direction { get; set; }

    /// <summary>The phrase for this side, such as "is blocked by".</summary>
    public required string Phrase { get; set; }

    /// <summary>Null when the caller may not see the other ticket; it still counts, but stays anonymous.</summary>
    public required TicketRefDto? Ticket { get; set; }
}

public class CreateTicketLinkInput
{
    public required Guid LinkTypeId { get; set; }

    /// <summary>Which phrase applies to this ticket: outward makes it the source.</summary>
    public TicketLinkDirection Direction { get; set; }

    /// <summary>The other ticket's key, such as SB-12; it may be in another space the caller can see.</summary>
    [Required]
    public required string TicketKey { get; set; }
}

public static class TicketLinkConsts
{
    public const int MaxNameLength = 64;
}

public static class TicketLinkErrors
{
    public const string Self = "SprintBoot:TicketLinkSelf";
    public const string Duplicate = "SprintBoot:TicketLinkDuplicate";
    public const string TypeInUse = "SprintBoot:TicketLinkTypeInUse";
    public const string TypeNameTaken = "SprintBoot:TicketLinkTypeNameTaken";
}

public interface ITicketLinkTypeAppService : IApplicationService
{
    Task<List<TicketLinkTypeDto>> GetListAsync();

    Task<TicketLinkTypeDto> CreateAsync(SaveTicketLinkTypeInput input);

    /// <summary>Renaming changes how every existing link of the type reads.</summary>
    Task<TicketLinkTypeDto> UpdateAsync(Guid id, SaveTicketLinkTypeInput input);

    /// <summary>Refused while any link uses the type.</summary>
    Task DeleteAsync(Guid id);
}

public interface ITicketLinkAppService : IApplicationService
{
    Task<List<TicketLinkDto>> GetListAsync(Guid ticketId);

    /// <summary>Needs edit rights on this ticket and view rights on the other.</summary>
    Task<TicketLinkDto> CreateAsync(Guid ticketId, CreateTicketLinkInput input);

    /// <summary>Allowed from either side for someone who may edit that side.</summary>
    Task DeleteAsync(Guid id);
}
