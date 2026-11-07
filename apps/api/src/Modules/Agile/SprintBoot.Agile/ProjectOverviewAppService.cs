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
/// For a sprint period the figures count the issues planned in those sprints, with their subtasks; epics are never in a
/// sprint. For a calendar period they count the issues created or updated in it.
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

        var issues = periodSprints.Count > 0
            ? await InSprintsAsync(all, periodSprints)
            : [.. all.Where(issue => InRange(issue.CreatedAt) || InRange(issue.UpdatedAt))];
        var completed = issues.Where(issue => InRange(issue.CompletedAt)).ToList();

        var (velocity, velocitySprints) = await VelocityAsync(all, boardSprints);
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
                LastCompletedAt = issues.Max(issue => issue.CompletedAt),
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

    /// <summary>Issues planned in the sprints, still there or finished there, and the subtasks under them.</summary>
    private async Task<List<IssueOverviewRow>> InSprintsAsync(List<IssueOverviewRow> all, List<Guid> sprintIds)
    {
        var planned = (await sprintIssues.GetListAsync(row => sprintIds.Contains(row.SprintId) && (row.RemovedAt == null || row.Outcome != SprintOutcome.Open)))
            .Select(row => row.IssueId)
            .ToHashSet();
        return [.. all.Where(issue => planned.Contains(issue.Id) || (issue.ParentId is { } parent && planned.Contains(parent) && issue.Hierarchy == IssueHierarchy.Subtask))];
    }

    /// <summary>
    /// Finished work per assignee over the last sprints. A closed sprint counts what it completed; the running one counts
    /// what is done so far, so its velocity grows as the sprint goes.
    /// </summary>
    private async Task<(List<(Guid UserId, decimal Points, int Issues)> Rows, int Sprints)> VelocityAsync(List<IssueOverviewRow> issues, List<Sprint> boardSprints)
    {
        var recent = boardSprints
            .Where(sprint => sprint.State is SprintState.Closed or SprintState.Active)
            .OrderByDescending(sprint => sprint.StartDate ?? DateOnly.MinValue)
            .ThenByDescending(sprint => sprint.CompletedAt)
            .Take(SprintCount)
            .ToList();
        if (recent.Count == 0)
        {
            return ([], 0);
        }

        var closedIds = recent.Where(sprint => sprint.State == SprintState.Closed).Select(sprint => sprint.Id).ToList();
        var activeIds = recent.Where(sprint => sprint.State == SprintState.Active).Select(sprint => sprint.Id).ToList();
        var finished = (await sprintIssues.GetListAsync(row => closedIds.Contains(row.SprintId) && row.Outcome == SprintOutcome.Completed))
            .Select(row => row.IssueId)
            .ToHashSet();
        var running = (await sprintIssues.GetListAsync(row => activeIds.Contains(row.SprintId) && row.RemovedAt == null))
            .Select(row => row.IssueId)
            .ToHashSet();

        var rows = issues
            .Where(issue => issue.AssigneeId is not null && (finished.Contains(issue.Id) || (running.Contains(issue.Id) && issue.Category == StatusCategory.Done)))
            .GroupBy(issue => issue.AssigneeId!.Value)
            .Select(group => (group.Key, group.Sum(issue => issue.StoryPoints ?? 0), group.Count()))
            .OrderByDescending(item => item.Item2)
            .ThenByDescending(item => item.Item3)
            .ToList();
        return (rows, recent.Count);
    }
}
