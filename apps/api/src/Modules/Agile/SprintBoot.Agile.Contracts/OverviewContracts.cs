using SprintBoot.Identity;
using SprintBoot.Issues;
using Volo.Abp.Application.Services;

namespace SprintBoot.Agile;

/// <summary>The stretch of time the space overview counts in.</summary>
public enum OverviewPeriod
{
    /// <summary>The running sprint; without one, the last two weeks.</summary>
    CurrentSprint = 0,

    /// <summary>From the start of the third most recent sprint, the running one included, until today.</summary>
    LastThreeSprints = 1,
    ThisMonth = 2,
    ThisQuarter = 3,
    ThisYear = 4,
}

public class OverviewRangeDto
{
    public required OverviewPeriod Period { get; set; }

    public required DateOnly From { get; set; }

    public required DateOnly To { get; set; }

    /// <summary>Set for the current sprint period when a sprint is running.</summary>
    public required string? SprintName { get; set; }

    /// <summary>Days left in the running sprint, counting today.</summary>
    public required int? DaysLeft { get; set; }

    /// <summary>
    /// True when the figures count the issues of the period's sprints (and their subtasks); otherwise they count the
    /// issues created or updated in the period.
    /// </summary>
    public required bool BySprint { get; set; }
}

public class OverviewStatusCountDto
{
    public required string Name { get; set; }

    public required int Count { get; set; }
}

public class OverviewIssueCountsDto
{
    public required int Total { get; set; }

    public required int CreatedInPeriod { get; set; }

    public required int ToDo { get; set; }

    public required int InProgress { get; set; }

    /// <summary>The in-progress count split by status, most first.</summary>
    public required List<OverviewStatusCountDto> InProgressByStatus { get; set; }

    public required int Done { get; set; }
}

public class OverviewTypeCountDto
{
    public required string Name { get; set; }

    public required string Icon { get; set; }

    public required IssueHierarchy Hierarchy { get; set; }

    public required int Count { get; set; }
}

public class OverviewActivityDto
{
    public required int Created { get; set; }

    public required int Updated { get; set; }

    public required int Completed { get; set; }

    public required DateTime? LastCreatedAt { get; set; }

    public required DateTime? LastUpdatedAt { get; set; }

    public required DateTime? LastCompletedAt { get; set; }
}

public class OverviewPullRequestsDto
{
    public required int Total { get; set; }

    public required int Open { get; set; }

    public required int Merged { get; set; }
}

public class OverviewVelocityDto
{
    public required UserRefDto User { get; set; }

    public required decimal Points { get; set; }

    public required int Issues { get; set; }
}

public class OverviewEventDto
{
    public required UserRefDto? Actor { get; set; }

    public required IssueEventKind Kind { get; set; }

    public required string IssueKey { get; set; }

    public required string IssueTitle { get; set; }

    public required string? Field { get; set; }

    public required string? NewValue { get; set; }

    public required DateTime At { get; set; }
}

public class OverviewSummaryDto
{
    public required OverviewRangeDto Range { get; set; }

    public required int Members { get; set; }

    public required int MembersAddedInPeriod { get; set; }

    public required OverviewIssueCountsDto Issues { get; set; }

    public required List<OverviewTypeCountDto> ByType { get; set; }

    public required OverviewActivityDto Activity { get; set; }

    public required OverviewPullRequestsDto PullRequests { get; set; }

    /// <summary>Finished work per assignee over the last three sprints, the running one included at its progress so far, most first.</summary>
    public required List<OverviewVelocityDto> Velocity { get; set; }

    public required int VelocitySprints { get; set; }

    public required List<OverviewEventDto> Recent { get; set; }
}

public class OverviewSummaryInput
{
    public OverviewPeriod Period { get; set; }
}

/// <summary>Figures for the Summary tab of a space's Overview.</summary>
public interface IProjectOverviewAppService : IApplicationService
{
    Task<OverviewSummaryDto> GetSummaryAsync(Guid projectId, OverviewSummaryInput input);
}
