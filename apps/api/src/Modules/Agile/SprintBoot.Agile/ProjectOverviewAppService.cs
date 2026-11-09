using Microsoft.AspNetCore.Authorization;
using SprintBoot.GitIntegration;
using SprintBoot.Identity;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace SprintBoot.Agile;

/// <summary>Gathers the Summary tab from issues, sprints, members and pull requests, each read through its owner.</summary>
/// <remarks>
/// For a sprint period the figures count the issues of those sprints, as the Board and Backlog show them: the ones still
/// in a running sprint and the ones a closed sprint ended with. Subtasks and epics are not sprint issues, so they do not
/// count. For a calendar period the figures count the issues created or updated in it.
/// </remarks>
[Authorize]
public class ProjectOverviewAppService(
    IRepository<Sprint, Guid> sprints,
    IRepository<SprintIssue, Guid> sprintIssues,
    BoardProvider boards,
    IIssueOverviewFacts issueFacts,
    IProjectGitFacts gitFacts,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    IClock clock)
    : ApplicationService, IProjectOverviewAppService
{
    private const int SprintCount = 3;
    private const int RecentCount = 5;
    private const int FallbackDays = 14;

    public async Task<OverviewSummaryDto> GetSummaryAsync(Guid projectId, OverviewSummaryInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var board = await boards.GetAsync(projectId);
        var boardSprints = await sprints.GetListAsync(sprint => sprint.BoardId == board.Id);
        var (range, periodSprints) = RangeOf(input.Period, boardSprints, DateOnly.FromDateTime(clock.Now));

        var all = await issueFacts.GetIssuesAsync(projectId);
        var from = range.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = range.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        bool InRange(DateTime? at) => at >= from && at < to;

        var bySprint = periodSprints.Count > 0;
        var sprintWork = bySprint ? await SprintWorkAsync(periodSprints, boardSprints) : null;
        List<IssueOverviewRow> issues = sprintWork is not null
            ? [.. all.Where(issue => sprintWork.Planned.Contains(issue.Id))]
            : all.Where(issue => InRange(issue.CreatedAt) || InRange(issue.UpdatedAt)).ToList();
        // Done in the sprint counts whenever it happened, even after the planned end; by date, done in the period.
        var completed = sprintWork is not null
            ? issues.Where(sprintWork.IsDone).ToList()
            : issues.Where(issue => InRange(issue.CompletedAt)).ToList();

        var (velocity, velocitySprints) = sprintWork is not null
            ? (VelocityOf(issues.Where(sprintWork.IsDone)), periodSprints.Count)
            : (VelocityOf(all.Where(issue => issue.Category == StatusCategory.Done && InRange(issue.CompletedAt))), 0);
        var events = await issueFacts.GetRecentEventsAsync(projectId, RecentCount);
        var people = await userDirectory.GetByIdsAsync([.. velocity.Select(item => item.UserId), .. events.Select(item => item.ActorId)]);
        var pulls = await gitFacts.GetPullRequestCountsAsync(projectId);
        var joined = await projectAccess.GetMemberJoinTimesAsync(projectId);

        return new OverviewSummaryDto
        {
            Range = range,
            Members = joined.Count,
            MembersAddedInPeriod = joined.Values.Count(at => InRange(at)),
            Issues = new OverviewIssueCountsDto
            {
                Total = issues.Count,
                CreatedInPeriod = issues.Count(issue => InRange(issue.CreatedAt)),
                ToDo = issues.Count(issue => issue.Category == StatusCategory.ToDo),
                InProgress = issues.Count(issue => issue.Category == StatusCategory.InProgress),
                InProgressByStatus = [.. issues
                    .Where(issue => issue.Category == StatusCategory.InProgress)
                    .GroupBy(issue => issue.StatusName)
                    .Select(group => new OverviewStatusCountDto { Name = group.Key, Count = group.Count() })
                    .OrderByDescending(item => item.Count)],
                Done = issues.Count(issue => issue.Category == StatusCategory.Done),
            },
            ByType = [.. issues
                .GroupBy(issue => (issue.TypeName, issue.TypeIcon, issue.Hierarchy))
                .Select(group => new OverviewTypeCountDto { Name = group.Key.TypeName, Icon = group.Key.TypeIcon, Hierarchy = group.Key.Hierarchy, Count = group.Count() })
                .OrderByDescending(item => item.Count)],
            Activity = new OverviewActivityDto
            {
                Created = issues.Count(issue => InRange(issue.CreatedAt)),
                Updated = issues.Count(issue => InRange(issue.UpdatedAt)),
                Completed = completed.Count,
                LastCreatedAt = issues.Count == 0 ? null : issues.Max(issue => issue.CreatedAt),
                LastUpdatedAt = issues.Count == 0 ? null : issues.Max(issue => issue.UpdatedAt),
                LastCompletedAt = completed.Max(issue => issue.CompletedAt),
            },
            PullRequests = new OverviewPullRequestsDto { Total = pulls.Total, Open = pulls.Open, Merged = pulls.Merged },
            Velocity = [.. velocity
                .Where(item => people.ContainsKey(item.UserId))
                .Select(item => new OverviewVelocityDto { User = people[item.UserId], Points = item.Points, Issues = item.Issues })],
            VelocitySprints = velocitySprints,
            Recent = [.. events.Select(item => new OverviewEventDto
            {
                Actor = people.GetValueOrDefault(item.ActorId),
                Kind = item.Kind,
                IssueKey = item.IssueKey,
                IssueTitle = item.IssueTitle,
                Field = item.Field,
                NewValue = item.NewValue,
                At = item.At,
            })],
        };
    }

    /// <summary>The period's dates, and the sprints whose issues it counts; no sprints means it counts by date.</summary>
    private static (OverviewRangeDto Range, List<Guid> Sprints) RangeOf(OverviewPeriod period, List<Sprint> boardSprints, DateOnly today)
    {
        OverviewRangeDto Range(DateOnly from, DateOnly to, bool bySprint, string? name = null, int? left = null) =>
            new() { Period = period, From = from, To = to, SprintName = name, DaysLeft = left, BySprint = bySprint };
        var fallback = (Range(today.AddDays(-FallbackDays + 1), today, false), new List<Guid>());

        switch (period)
        {
            case OverviewPeriod.CurrentSprint:
            {
                var active = boardSprints.FirstOrDefault(sprint => sprint.State == SprintState.Active);
                if (active?.StartDate is not { } start)
                {
                    return fallback;
                }

                var end = active.EndDate ?? today;
                return (Range(start, end, true, active.Name, Math.Max(0, end.DayNumber - today.DayNumber + 1)), [active.Id]);
            }

            case OverviewPeriod.LastThreeSprints:
            {
                var recent = boardSprints
                    .Where(sprint => sprint.State != SprintState.Future && sprint.StartDate is not null)
                    .OrderByDescending(sprint => sprint.StartDate)
                    .Take(SprintCount)
                    .ToList();
                if (recent.Count == 0)
                {
                    return fallback;
                }

                var last = recent.Max(sprint => sprint.EndDate ?? today);
                return (Range(recent.Min(sprint => sprint.StartDate!.Value), last > today ? last : today, true), [.. recent.Select(sprint => sprint.Id)]);
            }

            case OverviewPeriod.ThisMonth:
                return (Range(new DateOnly(today.Year, today.Month, 1), today, false), []);
            case OverviewPeriod.ThisQuarter:
                return (Range(new DateOnly(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1), today, false), []);
            default:
                return (Range(new DateOnly(today.Year, 1, 1), today, false), []);
        }
    }

    /// <summary>The issues of the sprints and which of them count as done there.</summary>
    private sealed record SprintWork(HashSet<Guid> Planned, HashSet<Guid> Finished, HashSet<Guid> Running)
    {
        /// <summary>A closed sprint counts what it completed; a running one counts what is done so far.</summary>
        public bool IsDone(IssueOverviewRow issue) => Finished.Contains(issue.Id) || (Running.Contains(issue.Id) && issue.Category == StatusCategory.Done);
    }

    /// <summary>
    /// Issues still in a running sprint, and the ones a closed sprint ended with, completed or not. An issue moved out of
    /// a sprint before it ended is no longer one of its issues.
    /// </summary>
    private async Task<SprintWork> SprintWorkAsync(List<Guid> sprintIds, List<Sprint> boardSprints)
    {
        var closed = boardSprints.Where(sprint => sprintIds.Contains(sprint.Id) && sprint.State == SprintState.Closed).Select(sprint => sprint.Id).ToHashSet();
        var rows = await sprintIssues.GetListAsync(row => sprintIds.Contains(row.SprintId) && (row.RemovedAt == null || row.Outcome != SprintOutcome.Open));
        return new SprintWork(
            [.. rows.Select(row => row.IssueId)],
            [.. rows.Where(row => closed.Contains(row.SprintId) && row.Outcome == SprintOutcome.Completed).Select(row => row.IssueId)],
            [.. rows.Where(row => !closed.Contains(row.SprintId) && row.RemovedAt == null).Select(row => row.IssueId)]);
    }

    /// <summary>Finished work per assignee: story points and issue count, most points first.</summary>
    private static List<(Guid UserId, decimal Points, int Issues)> VelocityOf(IEnumerable<IssueOverviewRow> done) =>
        [.. done
            .Where(issue => issue.AssigneeId is not null)
            .GroupBy(issue => issue.AssigneeId!.Value)
            .Select(group => (group.Key, group.Sum(issue => issue.StoryPoints ?? 0), group.Count()))
            .OrderByDescending(item => item.Item2)
            .ThenByDescending(item => item.Item3)];
}
