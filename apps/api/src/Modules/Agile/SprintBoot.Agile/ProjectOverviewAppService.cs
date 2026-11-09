using Microsoft.AspNetCore.Authorization;
using SprintBoot.GitIntegration;
using SprintBoot.Identity;
using SprintBoot.Tickets;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace SprintBoot.Agile;

/// <summary>Gathers the Summary tab from tickets, sprints, members and pull requests, each read through its owner.</summary>
/// <remarks>
/// For a sprint period the figures count the tickets of those sprints, as the Board and Backlog show them: the ones still
/// in a running sprint and the ones a closed sprint ended with. Subtasks and epics are not sprint tickets, so they do not
/// count. For a calendar period the figures count the tickets created or updated in it.
/// </remarks>
[Authorize]
public class ProjectOverviewAppService(
    IRepository<Sprint, Guid> sprints,
    IRepository<SprintTicket, Guid> sprintTickets,
    BoardProvider boards,
    ITicketOverviewFacts ticketFacts,
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
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        var board = await boards.GetAsync(projectId);
        var boardSprints = await sprints.GetListAsync(sprint => sprint.BoardId == board.Id);
        var (range, periodSprints) = RangeOf(input.Period, boardSprints, DateOnly.FromDateTime(clock.Now));

        var all = await ticketFacts.GetTicketsAsync(projectId);
        var from = range.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = range.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        bool InRange(DateTime? at) => at >= from && at < to;

        var bySprint = periodSprints.Count > 0;
        var sprintWork = bySprint ? await SprintWorkAsync(periodSprints, boardSprints) : null;
        List<TicketOverviewRow> tickets = sprintWork is not null
            ? [.. all.Where(ticket => sprintWork.Planned.Contains(ticket.Id))]
            : all.Where(ticket => InRange(ticket.CreatedAt) || InRange(ticket.UpdatedAt)).ToList();
        // Done in the sprint counts whenever it happened, even after the planned end; by date, done in the period.
        var completed = sprintWork is not null
            ? tickets.Where(sprintWork.IsDone).ToList()
            : tickets.Where(ticket => InRange(ticket.CompletedAt)).ToList();

        var (velocity, velocitySprints) = sprintWork is not null
            ? (VelocityOf(tickets.Where(sprintWork.IsDone)), periodSprints.Count)
            : (VelocityOf(all.Where(ticket => ticket.Category == StatusCategory.Done && InRange(ticket.CompletedAt))), 0);
        var events = await ticketFacts.GetRecentEventsAsync(projectId, RecentCount);
        var people = await userDirectory.GetByIdsAsync([.. velocity.Select(item => item.UserId), .. events.Select(item => item.ActorId)]);
        var pulls = await gitFacts.GetPullRequestCountsAsync(projectId);
        var joined = await projectAccess.GetMemberJoinTimesAsync(projectId);

        return new OverviewSummaryDto
        {
            Range = range,
            Members = joined.Count,
            MembersAddedInPeriod = joined.Values.Count(at => InRange(at)),
            Tickets = new OverviewTicketCountsDto
            {
                Total = tickets.Count,
                CreatedInPeriod = tickets.Count(ticket => InRange(ticket.CreatedAt)),
                ToDo = tickets.Count(ticket => ticket.Category == StatusCategory.ToDo),
                InProgress = tickets.Count(ticket => ticket.Category == StatusCategory.InProgress),
                InProgressByStatus = [.. tickets
                    .Where(ticket => ticket.Category == StatusCategory.InProgress)
                    .GroupBy(ticket => ticket.StatusName)
                    .Select(group => new OverviewStatusCountDto { Name = group.Key, Count = group.Count() })
                    .OrderByDescending(item => item.Count)],
                Done = tickets.Count(ticket => ticket.Category == StatusCategory.Done),
            },
            ByType = [.. tickets
                .GroupBy(ticket => (ticket.TypeName, ticket.TypeIcon, ticket.Hierarchy))
                .Select(group => new OverviewTypeCountDto { Name = group.Key.TypeName, Icon = group.Key.TypeIcon, Hierarchy = group.Key.Hierarchy, Count = group.Count() })
                .OrderByDescending(item => item.Count)],
            Activity = new OverviewActivityDto
            {
                Created = tickets.Count(ticket => InRange(ticket.CreatedAt)),
                Updated = tickets.Count(ticket => InRange(ticket.UpdatedAt)),
                Completed = completed.Count,
                LastCreatedAt = tickets.Count == 0 ? null : tickets.Max(ticket => ticket.CreatedAt),
                LastUpdatedAt = tickets.Count == 0 ? null : tickets.Max(ticket => ticket.UpdatedAt),
                LastCompletedAt = completed.Max(ticket => ticket.CompletedAt),
            },
            PullRequests = new OverviewPullRequestsDto { Total = pulls.Total, Open = pulls.Open, Merged = pulls.Merged },
            Velocity = [.. velocity
                .Where(item => people.ContainsKey(item.UserId))
                .Select(item => new OverviewVelocityDto { User = people[item.UserId], Points = item.Points, Tickets = item.Tickets })],
            VelocitySprints = velocitySprints,
            Recent = [.. events.Select(item => new OverviewEventDto
            {
                Actor = people.GetValueOrDefault(item.ActorId),
                Kind = item.Kind,
                TicketKey = item.TicketKey,
                TicketTitle = item.TicketTitle,
                Field = item.Field,
                NewValue = item.NewValue,
                At = item.At,
            })],
        };
    }

    /// <summary>The period's dates, and the sprints whose tickets it counts; no sprints means it counts by date.</summary>
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

    /// <summary>The tickets of the sprints and which of them count as done there.</summary>
    private sealed record SprintWork(HashSet<Guid> Planned, HashSet<Guid> Finished, HashSet<Guid> Running)
    {
        /// <summary>A closed sprint counts what it completed; a running one counts what is done so far.</summary>
        public bool IsDone(TicketOverviewRow ticket) => Finished.Contains(ticket.Id) || (Running.Contains(ticket.Id) && ticket.Category == StatusCategory.Done);
    }

    /// <summary>
    /// Tickets still in a running sprint, and the ones a closed sprint ended with, completed or not. A ticket moved out of
    /// a sprint before it ended is no longer one of its tickets.
    /// </summary>
    private async Task<SprintWork> SprintWorkAsync(List<Guid> sprintIds, List<Sprint> boardSprints)
    {
        var closed = boardSprints.Where(sprint => sprintIds.Contains(sprint.Id) && sprint.State == SprintState.Closed).Select(sprint => sprint.Id).ToHashSet();
        var rows = await sprintTickets.GetListAsync(row => sprintIds.Contains(row.SprintId) && (row.RemovedAt == null || row.Outcome != SprintOutcome.Open));
        return new SprintWork(
            [.. rows.Select(row => row.TicketId)],
            [.. rows.Where(row => closed.Contains(row.SprintId) && row.Outcome == SprintOutcome.Completed).Select(row => row.TicketId)],
            [.. rows.Where(row => !closed.Contains(row.SprintId) && row.RemovedAt == null).Select(row => row.TicketId)]);
    }

    /// <summary>Finished work per assignee: story points and ticket count, most points first.</summary>
    private static List<(Guid UserId, decimal Points, int Tickets)> VelocityOf(IEnumerable<TicketOverviewRow> done) =>
        [.. done
            .Where(ticket => ticket.AssigneeId is not null)
            .GroupBy(ticket => ticket.AssigneeId!.Value)
            .Select(group => (group.Key, group.Sum(ticket => ticket.StoryPoints ?? 0), group.Count()))
            .OrderByDescending(item => item.Item2)
            .ThenByDescending(item => item.Item3)];
}
