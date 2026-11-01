using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace SprintBoot.Agile;

public class SprintMapper(IRepository<SprintIssue, Guid> sprintIssues, IIssuePlanningGateway issues) : ITransientDependency
{
    public async Task<SprintSummaryDto> ToSummaryAsync(Sprint sprint, Guid projectId)
    {
        var rows = await sprintIssues.GetListAsync(item => item.SprintId == sprint.Id && item.RemovedAt == null);
        return Summary(sprint, await issues.GetCardsAsync(projectId, [.. rows.Select(item => item.IssueId)]));
    }

    public static SprintSummaryDto Summary(Sprint sprint, IReadOnlyCollection<IssueCardDto> cards) => new()
    {
        Id = sprint.Id,
        Name = sprint.Name,
        Goal = sprint.Goal,
        State = sprint.State,
        StartDate = sprint.StartDate,
        EndDate = sprint.EndDate,
        CompletedAt = sprint.CompletedAt,
        IssueCount = cards.Count,
        TotalPoints = cards.Sum(card => card.StoryPoints ?? 0),
        DonePoints = cards.Where(card => card.Status.Category == StatusCategory.Done).Sum(card => card.StoryPoints ?? 0),
    };
}

public static partial class SprintPlanner
{
    /// <summary>One past the highest trailing number, so "Sprint 2" is followed by "Sprint 3".</summary>
    public static string NextName(IEnumerable<string> names)
    {
        var numbers = names.Select(name => TrailingNumber().Match(name))
            .Where(match => match.Success && int.TryParse(match.Value, out _))
            .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToList();
        return $"Sprint {(numbers.Count == 0 ? 0 : numbers.Max()) + 1}";
    }

    /// <summary>The day after the last planned end, but never in the past.</summary>
    public static DateOnly NextStart(IEnumerable<DateOnly?> ends, DateOnly today)
    {
        var last = ends.Max();
        return last is { } end && end >= today ? end.AddDays(1) : today;
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingNumber();
}

[Authorize]
public class SprintAppService(
    IRepository<Sprint, Guid> sprints,
    IRepository<SprintIssue, Guid> sprintIssues,
    IRepository<Board, Guid> boardRepository,
    IRepository<BoardEntry, Guid> entries,
    BoardProvider boards,
    SprintMapper mapper,
    IIssuePlanningGateway issues,
    IProjectAccess projectAccess,
    IClock clock,
    BoardNotifier notifier,
    IDistributedEventBus eventBus)
    : ApplicationService, ISprintAppService
{
    private const string SprintField = "sprint";

    public async Task<BacklogDto> GetBacklogAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var board = await boards.GetAsync(projectId);

        if (board.Type == BoardType.Kanban)
        {
            var onBoard = (await entries.GetListAsync(entry => entry.BoardId == board.Id)).Select(entry => entry.IssueId).ToList();
            return new BacklogDto
            {
                BoardId = board.Id,
                Type = board.Type,
                Sprints = [],
                Backlog = await issues.GetBacklogCardsAsync(projectId, onBoard),
                ClosedSprints = [],
            };
        }

        var all = await sprints.GetListAsync(sprint => sprint.BoardId == board.Id);
        var open = all.Where(sprint => sprint.State != SprintState.Closed)
            .OrderByDescending(sprint => sprint.State).ThenBy(sprint => sprint.CreationTime).ToList();
        var openIds = open.Select(sprint => sprint.Id).ToList();
        var memberships = await sprintIssues.GetListAsync(item => openIds.Contains(item.SprintId) && item.RemovedAt == null);

        var planned = new List<SprintWithIssuesDto>();
        foreach (var sprint in open)
        {
            var cards = await issues.GetCardsAsync(projectId, [.. memberships.Where(item => item.SprintId == sprint.Id).Select(item => item.IssueId)]);
            planned.Add(new SprintWithIssuesDto { Sprint = SprintMapper.Summary(sprint, cards), Issues = cards });
        }

        var closed = new List<SprintSummaryDto>();
        foreach (var sprint in all.Where(sprint => sprint.State == SprintState.Closed).OrderByDescending(sprint => sprint.CompletedAt))
        {
            closed.Add(await mapper.ToSummaryAsync(sprint, projectId));
        }

        return new BacklogDto
        {
            BoardId = board.Id,
            Type = board.Type,
            Sprints = planned,
            Backlog = await issues.GetBacklogCardsAsync(projectId, [.. memberships.Select(item => item.IssueId)]),
            ClosedSprints = closed,
        };
    }

    public async Task<SprintDefaultsDto> GetNewDefaultsAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ManageSprints);
        var board = await boards.GetAsync(projectId);
        var list = await sprints.GetListAsync(item => item.BoardId == board.Id);
        var days = await projectAccess.GetSprintLengthDaysAsync(projectId);
        var start = SprintPlanner.NextStart(list.Select(item => item.EndDate), DateOnly.FromDateTime(clock.Now));
        return new SprintDefaultsDto
        {
            Name = SprintPlanner.NextName(list.Select(item => item.Name)),
            StartDate = start,
            EndDate = start.AddDays(days - 1),
            LengthDays = days,
        };
    }

    public async Task<SprintSummaryDto> CreateAsync(CreateSprintInput input)
    {
        await projectAccess.CheckAsync(input.ProjectId, ProjectPermissions.ManageSprints);
        var board = await boards.GetAsync(input.ProjectId);
        if (board.Type == BoardType.Kanban)
        {
            throw new BusinessException(AgileErrors.KanbanHasNoSprints);
        }

        EnsureDates(input);
        var sprint = new Sprint(GuidGenerator.Create(), board.Id, input.Name)
        {
            Goal = input.Goal,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
        };
        await sprints.InsertAsync(sprint, autoSave: true);
        await notifier.ChangedAsync(input.ProjectId);
        return SprintMapper.Summary(sprint, []);
    }

    public async Task<SprintSummaryDto> UpdateAsync(Guid id, SaveSprintInput input)
    {
        var (sprint, projectId) = await LoadForManagingAsync(id);
        EnsureDates(input);
        var previousEnd = sprint.EndDate;
        sprint.Name = input.Name;
        sprint.Goal = input.Goal;
        sprint.StartDate = input.StartDate;
        sprint.EndDate = input.EndDate;
        await sprints.UpdateAsync(sprint, autoSave: true);
        await PushFollowingAsync(sprint, previousEnd);
        await notifier.ChangedAsync(projectId);
        return await mapper.ToSummaryAsync(sprint, projectId);
    }

    /// <summary>A sprint that ends later moves the planned ones after it by the same number of days.</summary>
    private async Task PushFollowingAsync(Sprint sprint, DateOnly? previousEnd)
    {
        if (previousEnd is not { } before || sprint.EndDate is not { } after || after <= before)
        {
            return;
        }

        var days = after.DayNumber - before.DayNumber;
        var following = await sprints.GetListAsync(item =>
            item.BoardId == sprint.BoardId && item.Id != sprint.Id && item.State == SprintState.Future && item.StartDate > before);
        foreach (var item in following)
        {
            item.StartDate = item.StartDate?.AddDays(days);
            item.EndDate = item.EndDate?.AddDays(days);
        }

        await sprints.UpdateManyAsync(following, autoSave: true);
    }

    public async Task DeleteAsync(Guid id)
    {
        var (sprint, projectId) = await LoadForManagingAsync(id);
        if (sprint.State != SprintState.Future)
        {
            throw new BusinessException(AgileErrors.SprintNotFuture);
        }

        // Issues simply lose their membership, which returns them to the backlog.
        await sprintIssues.DeleteAsync(item => item.SprintId == id);
        await sprints.DeleteAsync(sprint);
        await CurrentUnitOfWork!.SaveChangesAsync();
        await notifier.ChangedAsync(projectId);
    }

    public async Task<SprintSummaryDto> StartAsync(Guid id, SaveSprintInput input)
    {
        var (sprint, projectId) = await LoadForManagingAsync(id);
        if (sprint.State != SprintState.Future)
        {
            throw new BusinessException(AgileErrors.SprintNotFuture);
        }

        if (string.IsNullOrWhiteSpace(input.Name) || input.StartDate is null || input.EndDate is null)
        {
            throw new BusinessException(AgileErrors.SprintDetailsRequired);
        }

        EnsureDates(input);
        if (await sprints.AnyAsync(item => item.BoardId == sprint.BoardId && item.State == SprintState.Active))
        {
            throw new BusinessException(AgileErrors.ActiveSprintExists);
        }

        var previousEnd = sprint.EndDate;
        sprint.Name = input.Name;
        sprint.Goal = input.Goal;
        sprint.StartDate = input.StartDate;
        sprint.EndDate = input.EndDate;
        sprint.State = SprintState.Active;
        await sprints.UpdateAsync(sprint, autoSave: true);
        await PushFollowingAsync(sprint, previousEnd);
        await PublishStateAsync(sprint, projectId);
        await notifier.ChangedAsync(projectId);
        return await mapper.ToSummaryAsync(sprint, projectId);
    }

    public async Task<CompleteSprintPreviewDto> GetCompletePreviewAsync(Guid id)
    {
        var (_, projectId) = await LoadForManagingAsync(id);
        var cards = await CurrentCardsAsync(id, projectId);
        var done = cards.Count(card => card.Status.Category == StatusCategory.Done);
        return new CompleteSprintPreviewDto { DoneCount = done, OpenCount = cards.Count - done };
    }

    public async Task<SprintSummaryDto> CompleteAsync(Guid id, CompleteSprintInput input)
    {
        var (sprint, projectId) = await LoadForManagingAsync(id);
        if (sprint.State != SprintState.Active)
        {
            throw new BusinessException(AgileErrors.SprintNotActive);
        }

        var cards = (await CurrentCardsAsync(id, projectId)).ToDictionary(card => card.Id);
        var hasOpen = cards.Values.Any(card => card.Status.Category != StatusCategory.Done);
        if (hasOpen && input.TargetSprintId is null && !input.ToBacklog)
        {
            throw new BusinessException(AgileErrors.CompletionTargetRequired);
        }

        Sprint? target = null;
        if (hasOpen && input.TargetSprintId is { } targetId)
        {
            target = await sprints.GetAsync(targetId);
            if (target.BoardId != sprint.BoardId || target.State != SprintState.Future)
            {
                throw new BusinessException(AgileErrors.SprintNotFuture);
            }
        }

        var now = clock.Now;
        foreach (var row in await sprintIssues.GetListAsync(item => item.SprintId == id && item.RemovedAt == null))
        {
            var isDone = cards.TryGetValue(row.IssueId, out var card) && card.Status.Category == StatusCategory.Done;
            row.Outcome = isDone ? SprintOutcome.Completed : SprintOutcome.NotCompleted;
            if (!isDone)
            {
                row.RemovedAt = now;
                if (target is not null)
                {
                    await sprintIssues.InsertAsync(new SprintIssue(GuidGenerator.Create(), target.Id, row.IssueId, now));
                }
            }

            await sprintIssues.UpdateAsync(row);
        }

        sprint.State = SprintState.Closed;
        sprint.CompletedAt = now;
        await sprints.UpdateAsync(sprint, autoSave: true);
        await PublishStateAsync(sprint, projectId);
        await notifier.ChangedAsync(projectId);
        return await mapper.ToSummaryAsync(sprint, projectId);
    }

    public async Task<ClosedSprintDto> GetClosedAsync(Guid id)
    {
        var sprint = await sprints.GetAsync(id);
        var board = await boardRepository.GetAsync(sprint.BoardId);
        await projectAccess.CheckAsync(board.ProjectId, ProjectPermissions.ViewIssues);

        var rows = await sprintIssues.GetListAsync(item => item.SprintId == id);
        var completed = rows.Where(row => row.Outcome == SprintOutcome.Completed).Select(row => row.IssueId).ToList();
        var notCompleted = rows.Where(row => row.Outcome == SprintOutcome.NotCompleted).Select(row => row.IssueId).ToList();
        var completedCards = await issues.GetCardsAsync(board.ProjectId, completed);

        return new ClosedSprintDto
        {
            Sprint = SprintMapper.Summary(sprint, completedCards),
            Completed = completedCards,
            NotCompleted = await issues.GetCardsAsync(board.ProjectId, notCompleted),
        };
    }

    public async Task MoveIssueAsync(Guid issueId, MoveIssueInput input)
    {
        var projectId = await issues.GetProjectIdAsync(issueId);
        await projectAccess.CheckAsync(projectId, ProjectPermissions.RankBacklog);
        var card = await issues.FindCardAsync(issueId) ?? throw new EntityNotFoundException(typeof(SprintIssue), issueId);
        if (card.Type.Hierarchy != IssueHierarchy.Standard)
        {
            throw new BusinessException(AgileErrors.NotPlannable);
        }

        var board = await boards.GetAsync(projectId);
        var boardSprints = (await sprints.GetListAsync(sprint => sprint.BoardId == board.Id)).ToDictionary(sprint => sprint.Id);
        var openSprintIds = boardSprints.Values.Where(sprint => sprint.State != SprintState.Closed).Select(sprint => sprint.Id).ToList();
        var current = await sprintIssues.FirstOrDefaultAsync(item =>
            item.IssueId == issueId && item.RemovedAt == null && openSprintIds.Contains(item.SprintId));

        if (current?.SprintId != input.SprintId)
        {
            Sprint? destination = null;
            if (input.SprintId is { } sprintId)
            {
                destination = boardSprints.GetValueOrDefault(sprintId);
                if (destination is null || destination.State == SprintState.Closed)
                {
                    throw new BusinessException(AgileErrors.SprintNotFuture);
                }

                await sprintIssues.InsertAsync(new SprintIssue(GuidGenerator.Create(), sprintId, issueId, clock.Now));
            }

            var previous = current is null ? null : boardSprints[current.SprintId];
            if (current is not null)
            {
                current.RemovedAt = clock.Now;
                await sprintIssues.UpdateAsync(current);
            }

            await issues.RecordActivityAsync(issueId, SprintField, previous?.Name, destination?.Name);
        }

        if (input.AfterIssueId is not null || input.BeforeIssueId is not null)
        {
            await issues.MoveRankAsync(issueId, input.AfterIssueId, input.BeforeIssueId);
        }

        await CurrentUnitOfWork!.SaveChangesAsync();
        await notifier.ChangedAsync(projectId);
    }

    public async Task<SprintSummaryDto?> GetForIssueAsync(Guid issueId)
    {
        var projectId = await issues.GetProjectIdAsync(issueId);
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var rows = await sprintIssues.GetListAsync(item => item.IssueId == issueId && item.RemovedAt == null);
        var ids = rows.Select(row => row.SprintId).ToList();
        var sprint = (await sprints.GetListAsync(item => ids.Contains(item.Id)))
            .OrderBy(item => item.State == SprintState.Closed).ThenByDescending(item => item.CreationTime).FirstOrDefault();
        return sprint is null ? null : await mapper.ToSummaryAsync(sprint, projectId);
    }

    private Task PublishStateAsync(Sprint sprint, Guid projectId) => eventBus.PublishAsync(
        new SprintStateChangedEto
        {
            EventId = GuidGenerator.Create(),
            ProjectId = projectId,
            ActorId = CurrentUser.GetId(),
            SprintName = sprint.Name,
            State = sprint.State,
        },
        useOutbox: true);

    private async Task<(Sprint Sprint, Guid ProjectId)> LoadForManagingAsync(Guid id)
    {
        var sprint = await sprints.GetAsync(id);
        var board = await boardRepository.GetAsync(sprint.BoardId);
        await projectAccess.CheckAsync(board.ProjectId, ProjectPermissions.ManageSprints);
        return (sprint, board.ProjectId);
    }

    private async Task<List<IssueCardDto>> CurrentCardsAsync(Guid sprintId, Guid projectId)
    {
        var rows = await sprintIssues.GetListAsync(item => item.SprintId == sprintId && item.RemovedAt == null);
        return await issues.GetCardsAsync(projectId, [.. rows.Select(row => row.IssueId)]);
    }

    private static void EnsureDates(SaveSprintInput input)
    {
        if (input.StartDate.HasValue && input.EndDate.HasValue && input.EndDate < input.StartDate)
        {
            throw new BusinessException(AgileErrors.SprintEndBeforeStart);
        }
    }
}
