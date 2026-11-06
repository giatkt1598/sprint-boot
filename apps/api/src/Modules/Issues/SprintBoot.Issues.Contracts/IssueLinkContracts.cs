using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Services;

namespace SprintBoot.Issues;

/// <summary>A kind of relationship between two issues, read one way from each side: "blocks" and "is blocked by".</summary>
public class IssueLinkTypeDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>How the source issue reads: "blocks".</summary>
    public required string Outward { get; set; }

    /// <summary>How the target issue reads: "is blocked by".</summary>
    public required string Inward { get; set; }

    public required int LinkCount { get; set; }
}

public class SaveIssueLinkTypeInput
{
    [Required]
    [StringLength(IssueLinkConsts.MaxNameLength)]
    public required string Name { get; set; }

    [Required]
    [StringLength(IssueLinkConsts.MaxNameLength)]
    public required string Outward { get; set; }

    [Required]
    [StringLength(IssueLinkConsts.MaxNameLength)]
    public required string Inward { get; set; }
}

public enum IssueLinkDirection
{
    /// <summary>This issue is the source: it "blocks" the other.</summary>
    Outward = 0,

    /// <summary>This issue is the target: it "is blocked by" the other.</summary>
    Inward = 1,
}

/// <summary>One link as seen from one issue.</summary>
public class IssueLinkDto
{
    public required Guid Id { get; set; }

    public required Guid LinkTypeId { get; set; }

    public required IssueLinkDirection Direction { get; set; }

    /// <summary>The phrase for this side, such as "is blocked by".</summary>
    public required string Phrase { get; set; }

    /// <summary>Null when the caller may not see the other issue; it still counts, but stays anonymous.</summary>
    public required IssueRefDto? Issue { get; set; }
}

public class CreateIssueLinkInput
{
    public required Guid LinkTypeId { get; set; }

    /// <summary>Which phrase applies to this issue: outward makes it the source.</summary>
    public IssueLinkDirection Direction { get; set; }

    /// <summary>The other issue's key, such as SB-12; it may be in another space the caller can see.</summary>
    [Required]
    public required string IssueKey { get; set; }
}

public static class IssueLinkConsts
{
    public const int MaxNameLength = 64;
}

public static class IssueLinkErrors
{
    public const string Self = "SprintBoot:IssueLinkSelf";
    public const string Duplicate = "SprintBoot:IssueLinkDuplicate";
    public const string TypeInUse = "SprintBoot:IssueLinkTypeInUse";
    public const string TypeNameTaken = "SprintBoot:IssueLinkTypeNameTaken";
}

public interface IIssueLinkTypeAppService : IApplicationService
{
    Task<List<IssueLinkTypeDto>> GetListAsync();

    Task<IssueLinkTypeDto> CreateAsync(SaveIssueLinkTypeInput input);

    /// <summary>Renaming changes how every existing link of the type reads.</summary>
    Task<IssueLinkTypeDto> UpdateAsync(Guid id, SaveIssueLinkTypeInput input);

    /// <summary>Refused while any link uses the type.</summary>
    Task DeleteAsync(Guid id);
}

public interface IIssueLinkAppService : IApplicationService
{
    Task<List<IssueLinkDto>> GetListAsync(Guid issueId);

    /// <summary>Needs edit rights on this issue and view rights on the other.</summary>
    Task<IssueLinkDto> CreateAsync(Guid issueId, CreateIssueLinkInput input);

    /// <summary>Allowed from either side for someone who may edit that side.</summary>
    Task DeleteAsync(Guid id);
}
