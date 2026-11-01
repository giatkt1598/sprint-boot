using Microsoft.AspNetCore.Authorization;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SprintBoot.Agile;

[Authorize]
public class BoardAppService(
    IRepository<BoardColumn, Guid> columns,
    IRepository<BoardColumnStatus, Guid> columnStatuses,
    IRepository<BoardEntry, Guid> entries,
    IRepository<Sprint, Guid> sprints,
    IRepository<SprintIssue, Guid> sprintIssues,
    BoardProvider boards,
    IRepository<Board, Guid> boardRepository,
    SprintMapper sprintMapper,
    IIssuePlanningGateway issues,
    IStatusLookup statusLookup,
    IWorkflowRuntime workflowRuntime,
    IProjectAccess projectAccess,
    BoardNotifier notifier)
    : ApplicationService, IBoardAppService
{
    public async Task<BoardViewDto> GetViewAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var board = await boards.GetAsync(projectId);
        var statuses = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);

        var columnList = (await columns.GetListAsync(column => column.BoardId == board.Id)).OrderBy(column => column.Order).ToList();
        var columnIds = columnList.Select(column => column.Id).ToList();
        var links = await columnStatuses.GetListAsync(link => columnIds.Contains(link.ColumnId));

        var used = (await workflowRuntime.GetProjectStatusIdsAsync(projectId)).ToHashSet();
        var mapped = links.Select(link => link.StatusId).ToHashSet();

        var active = await sprints.FirstOrDefaultAsync(sprint => sprint.BoardId == board.Id && sprint.State == SprintState.Active);
        List<Guid> issueIds;
        if (board.Type == BoardType.Kanban)
        {
            issueIds = [.. (await entries.GetListAsync(entry => entry.BoardId == board.Id)).Select(entry => entry.IssueId)];
        }
        else if (active is not null)
        {
            issueIds = [.. (await sprintIssues.GetListAsync(item => item.SprintId == active.Id && item.RemovedAt == null)).Select(item => item.IssueId)];
        }
        else
        {
            issueIds = [];
        }

        return new BoardViewDto
        {
            Id = board.Id,
            Type = board.Type,
            ActiveSprint = active is null ? null : await sprintMapper.ToSummaryAsync(active, projectId),
            Columns = [.. columnList.Select(column => new BoardColumnDto
            {
                Id = column.Id,
                Name = column.Name,
                MinLimit = column.MinLimit,
                MaxLimit = column.MaxLimit,
                IsHidden = column.IsHidden,
                Statuses = [.. links.Where(link => link.ColumnId == column.Id && statuses.ContainsKey(link.StatusId)).Select(link => statuses[link.StatusId])],
            })],
            CardFields = [.. board.CardFields?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? BoardCardFields.Default],
            Cards = await issues.GetCardsAsync(projectId, issueIds),
            UnmappedStatuses = [.. used.Where(id => !mapped.Contains(id) && statuses.ContainsKey(id)).Select(id => statuses[id])],
            StaleStatuses = [.. mapped.Where(id => !used.Contains(id) && statuses.ContainsKey(id)).Select(id => statuses[id])],
        };
    }

    public async Task<BoardViewDto> UpdateColumnsAsync(Guid projectId, SaveBoardInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);

        var statusIds = input.Columns.SelectMany(column => column.StatusIds).ToList();
        if (statusIds.Count != statusIds.Distinct().Count())
        {
            throw new BusinessException(AgileErrors.StatusInTwoColumns);
        }

        var board = await boards.GetAsync(projectId);
        var existing = await columns.GetListAsync(column => column.BoardId == board.Id);
        var existingIds = existing.Select(column => column.Id).ToList();
        await columnStatuses.DeleteAsync(link => existingIds.Contains(link.ColumnId), autoSave: true);
        await columns.DeleteAsync(column => column.BoardId == board.Id, autoSave: true);

        if (input.CardFields is { } fields)
        {
            // Stored in the catalogue's order, whatever order they were ticked in.
            board.CardFields = string.Join(',', BoardCardFields.All.Where(fields.Contains));
            await boardRepository.UpdateAsync(board);
        }

        var order = 0;
        foreach (var item in input.Columns)
        {
            var column = await columns.InsertAsync(new BoardColumn(GuidGenerator.Create(), board.Id, item.Name, order++, item.MinLimit, item.MaxLimit, item.IsHidden));
            await columnStatuses.InsertManyAsync(item.StatusIds.Select(id => new BoardColumnStatus(GuidGenerator.Create(), column.Id, id)));
        }

        await CurrentUnitOfWork!.SaveChangesAsync();
        await notifier.ChangedAsync(projectId);
        return await GetViewAsync(projectId);
    }

    public async Task SetEntryAsync(Guid projectId, Guid issueId, SetBoardEntryInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.RankBacklog);
        if (await issues.GetProjectIdAsync(issueId) != projectId)
        {
            throw new BusinessException(AgileErrors.NotPlannable);
        }

        var board = await boards.GetAsync(projectId);
        var entry = await entries.FirstOrDefaultAsync(item => item.BoardId == board.Id && item.IssueId == issueId);
        if (input.OnBoard && entry is null)
        {
            await entries.InsertAsync(new BoardEntry(GuidGenerator.Create(), board.Id, issueId), autoSave: true);
        }
        else if (!input.OnBoard && entry is not null)
        {
            await entries.DeleteAsync(entry, autoSave: true);
        }

        await notifier.ChangedAsync(projectId);
    }

    public async Task RankAsync(Guid issueId, RankIssueInput input)
    {
        var projectId = await issues.GetProjectIdAsync(issueId);
        await projectAccess.CheckAsync(projectId, ProjectPermissions.RankBacklog);
        await issues.MoveRankAsync(issueId, input.AfterIssueId, input.BeforeIssueId);
        await notifier.ChangedAsync(projectId);
    }
}
