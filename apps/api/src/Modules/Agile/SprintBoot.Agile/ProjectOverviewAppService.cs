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
    private const int VelocitySprintCount = 3;
    private const int RecentCount = 8;
    private const int FallbackDays = 14;

    public async Task<OverviewSummaryDto> GetSummaryAsync(Guid projectId, OverviewSummaryInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var board = await boards.GetAsync(projectId);
        var boardSprints = await sprints.GetListAsync(sprint => sprint.BoardId == board.Id);
        var range = RangeOf(input.Period, boardSprints, DateOnly.FromDateTime(clock.Now));

        var issues = await issueFacts.GetIssuesAsync(projectId);
        var from = range.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = range.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        bool InRange(DateTime? at) => at >= from && at < to;

        var velocity = await VelocityAsync(issues, boardSprints);
        var events = await issueFacts.GetRecentEventsAsync(projectId, RecentCount);
        var people = await userDirectory.GetByIdsAsync([.. velocity.Select(item => item.UserId), .. events.Select(item => item.ActorId)]);
        var pulls = await gitFacts.GetPullRequestCountsAsync(projectId);

        return new OverviewSummaryDto
        {
            Range = range,
            Members = (await projectAccess.GetMemberRolesAsync(projectId)).Count,
            Issues = new OverviewIssueCountsDto
            {
                Total = issues.Count,
                CreatedInPeriod = issues.Count(issue => InRange(issue.CreatedAt)),
                ToDo = issues.Count(issue => issue.Category == StatusCategory.ToDo),
                InProgress = issues.Count(issue => issue.Category == StatusCategory.InProgress),
                Done = issues.Count(issue => issue.Category == StatusCategory.Done),
            },
            ByType = [.. issues
                .GroupBy(issue => (issue.TypeName, issue.TypeIcon, issue.Hierarchy))
                .Select(group => new OverviewTypeCountDto { Name = group.Key.TypeName, Icon = group.Key.TypeIcon, Hierarchy = group.Key.Hierarchy, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)],
            Activity = new OverviewActivityDto
            {
                Created = issues.Count(issue => InRange(issue.CreatedAt)),
                Updated = issues.Count(issue => InRange(issue.UpdatedAt)),
                Completed = issues.Count(issue => InRange(issue.CompletedAt)),
                LastCreatedAt = issues.Count == 0 ? null : issues.Max(issue => issue.CreatedAt),
                LastUpdatedAt = issues.Count == 0 ? null : issues.Max(issue => issue.UpdatedAt),
                LastCompletedAt = issues.Max(issue => issue.CompletedAt),
            },
            PullRequests = new OverviewPullRequestsDto { Total = pulls.Total, Open = pulls.Open, Merged = pulls.Merged },
            Velocity = [.. velocity
                .Where(item => people.ContainsKey(item.UserId))
                .Select(item => new OverviewVelocityDto { User = people[item.UserId], Points = item.Points, Issues = item.Issues })],
            VelocitySprints = Math.Min(VelocitySprintCount, boardSprints.Count(sprint => sprint.State == SprintState.Closed)),
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

    private static OverviewRangeDto RangeOf(OverviewPeriod period, List<Sprint> boardSprints, DateOnly today)
    {
        var active = boardSprints.FirstOrDefault(sprint => sprint.State == SprintState.Active);
        OverviewRangeDto Range(DateOnly from, DateOnly to, string? name = null, int? left = null) =>
            new() { Period = period, From = from, To = to, SprintName = name, DaysLeft = left };

        switch (period)
        {
            case OverviewPeriod.LastThreeSprints:
            {
                var recent = boardSprints
                    .Where(sprint => sprint.State != SprintState.Future && sprint.StartDate is not null)
                    .OrderByDescending(sprint => sprint.StartDate)
                    .Take(VelocitySprintCount)
                    .ToList();
                return recent.Count == 0 ? Range(today.AddDays(-FallbackDays + 1), today) : Range(recent.Min(sprint => sprint.StartDate!.Value), today);
            }

            case OverviewPeriod.ThisMonth:
                return Range(new DateOnly(today.Year, today.Month, 1), today);
            case OverviewPeriod.ThisQuarter:
                return Range(new DateOnly(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1), today);
            case OverviewPeriod.ThisYear:
                return Range(new DateOnly(today.Year, 1, 1), today);
            default:
                if (active?.StartDate is { } start)
                {
                    var end = active.EndDate ?? today;
                    return Range(start, end, active.Name, Math.Max(0, end.DayNumber - today.DayNumber + 1));
                }

                return Range(today.AddDays(-FallbackDays + 1), today);
        }
    }

    private async Task<List<(Guid UserId, decimal Points, int Issues)>> VelocityAsync(List<IssueOverviewRow> issues, List<Sprint> boardSprints)
    {
        var closed = boardSprints
            .Where(sprint => sprint.State == SprintState.Closed)
            .OrderByDescending(sprint => sprint.CompletedAt)
            .Take(VelocitySprintCount)
            .Select(sprint => sprint.Id)
            .ToList();
        if (closed.Count == 0)
        {
            return [];
        }

        var completed = (await sprintIssues.GetListAsync(row => closed.Contains(row.SprintId) && row.Outcome == SprintOutcome.Completed))
            .Select(row => row.IssueId)
            .ToHashSet();
        return [.. issues
            .Where(issue => completed.Contains(issue.Id) && issue.AssigneeId is not null)
            .GroupBy(issue => issue.AssigneeId!.Value)
            .Select(group => (group.Key, group.Sum(issue => issue.StoryPoints ?? 0), group.Count()))
            .OrderByDescending(item => item.Item2)
            .ThenByDescending(item => item.Item3)];
    }
}
