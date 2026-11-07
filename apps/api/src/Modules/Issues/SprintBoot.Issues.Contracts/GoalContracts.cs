using System.ComponentModel.DataAnnotations;
using SprintBoot.Identity;
using Volo.Abp.Application.Services;

namespace SprintBoot.Issues;

/// <summary>Where a goal stands, as its owner last reported.</summary>
public enum GoalStatus
{
    Pending = 0,
    OnTrack = 1,
    AtRisk = 2,
    OffTrack = 3,
    Paused = 4,
    Done = 5,
}

public class GoalDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Sanitized rich text HTML; empty when there is none.</summary>
    public required string Description { get; set; }

    public required UserRefDto? Owner { get; set; }

    public required DateOnly? StartDate { get; set; }

    public required DateOnly? TargetDate { get; set; }

    public required GoalStatus Status { get; set; }

    /// <summary>0 to 100, as the owner last reported it.</summary>
    public required int Progress { get; set; }

    /// <summary>Set once the goal is archived; archived goals are read only.</summary>
    public required DateTime? ArchivedAt { get; set; }

    public required DateTime CreatedAt { get; set; }

    /// <summary>When the owner last reported on it; null before the first update.</summary>
    public required DateTime? LastUpdatedAt { get; set; }

    /// <summary>Whether the reader may change it.</summary>
    public required bool CanEdit { get; set; }
}

public class SaveGoalInput
{
    [Required]
    [StringLength(GoalConsts.MaxNameLength, MinimumLength = 1)]
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>A member of the space; empty means nobody owns it yet.</summary>
    public Guid? OwnerId { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? TargetDate { get; set; }
}

/// <summary>A report on the goal: where it stands now, how far along, and a word on why.</summary>
public class AddGoalUpdateInput
{
    public required GoalStatus Status { get; set; }

    [Range(0, 100)]
    public required int Progress { get; set; }

    [StringLength(GoalConsts.MaxNoteLength)]
    public string? Note { get; set; }
}

public class GoalUpdateDto
{
    public required Guid Id { get; set; }

    public required UserRefDto? Author { get; set; }

    public required GoalStatus Status { get; set; }

    public required int Progress { get; set; }

    public required string? Note { get; set; }

    public required DateTime CreatedAt { get; set; }
}

public static class GoalConsts
{
    public const int MaxNameLength = 128;
    public const int MaxNoteLength = 1000;
}

public static class GoalErrors
{
    public const string DatesInvalid = "SprintBoot:GoalDatesInvalid";
    public const string OwnerNotMember = "SprintBoot:GoalOwnerNotMember";
    public const string Archived = "SprintBoot:GoalArchived";
}

/// <summary>The goals of a space. Readers of its issues read them; its admins write them.</summary>
public interface IGoalAppService : IApplicationService
{
    /// <summary>The space's active goals, or its archived ones.</summary>
    Task<List<GoalDto>> GetListAsync(Guid projectId, bool archived = false);

    Task<GoalDto> CreateAsync(Guid projectId, SaveGoalInput input);

    /// <summary>Name, description, owner and dates; status and progress change through updates.</summary>
    Task<GoalDto> UpdateAsync(Guid id, SaveGoalInput input);

    Task<GoalDto> AddUpdateAsync(Guid id, AddGoalUpdateInput input);

    /// <summary>The reports on a goal, newest first.</summary>
    Task<List<GoalUpdateDto>> GetUpdatesAsync(Guid id);

    Task<GoalDto> ArchiveAsync(Guid id);

    Task<GoalDto> UnarchiveAsync(Guid id);

    Task DeleteAsync(Guid id);
}
