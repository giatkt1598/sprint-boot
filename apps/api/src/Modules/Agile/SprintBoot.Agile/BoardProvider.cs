using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Guids;

namespace SprintBoot.Agile;

public class BoardProvider(
    IRepository<Board, Guid> boards,
    IRepository<BoardColumn, Guid> columns,
    IRepository<BoardColumnStatus, Guid> columnStatuses,
    IStatusLookup statusLookup,
    IProjectAccess projectAccess,
    IGuidGenerator guids) : ITransientDependency
{
    public Task<Board> GetAsync(Guid projectId) => boards.GetAsync(board => board.ProjectId == projectId);

    /// <summary>One column per default status; admins reshape it in Space settings.</summary>
    public async Task CreateDefaultAsync(Guid projectId)
    {
        if (await boards.AnyAsync(board => board.ProjectId == projectId))
        {
            return;
        }

        var project = await projectAccess.GetRefAsync(projectId);
        var board = await boards.InsertAsync(new Board(guids.Create(), projectId, project.Name, project.Type), autoSave: true);
        var order = 0;
        // A new space has no statuses of its own, and other spaces' are not for it.
        foreach (var status in (await statusLookup.GetListAsync()).Where(item => item.ProjectId is null))
        {
            var column = await columns.InsertAsync(new BoardColumn(guids.Create(), board.Id, status.Name, order++, null, null));
            await columnStatuses.InsertAsync(new BoardColumnStatus(guids.Create(), column.Id, status.Id));
        }
    }
}

public class ProjectBoardHandler(BoardProvider provider) : ILocalEventHandler<ProjectCreatedEvent>, ITransientDependency
{
    public Task HandleEventAsync(ProjectCreatedEvent eventData) => provider.CreateDefaultAsync(eventData.ProjectId);
}

/// <summary>Columns follow a deleted status onto its replacement, unless the board already shows that one.</summary>
public class BoardStatusRemovedHandler(
    IRepository<BoardColumn, Guid> columns,
    IRepository<BoardColumnStatus, Guid> columnStatuses,
    IGuidGenerator guids)
    : ILocalEventHandler<StatusRemovedEvent>, ITransientDependency
{
    public async Task HandleEventAsync(StatusRemovedEvent eventData)
    {
        var mapped = await columnStatuses.GetListAsync(item => item.StatusId == eventData.StatusId);
        if (eventData.ReplacementId is { } replacement)
        {
            var columnIds = mapped.Select(item => item.ColumnId).ToList();
            var boards = (await columns.GetListAsync(column => columnIds.Contains(column.Id))).ToDictionary(column => column.Id, column => column.BoardId);
            var boardIds = boards.Values.Distinct().ToList();
            var siblings = await columns.GetListAsync(column => boardIds.Contains(column.BoardId));
            var siblingIds = siblings.Select(column => column.Id).ToList();
            var taken = await columnStatuses.GetListAsync(item => item.StatusId == replacement && siblingIds.Contains(item.ColumnId));
            var covered = taken.Select(item => siblings.First(column => column.Id == item.ColumnId).BoardId).ToHashSet();

            foreach (var item in mapped.Where(item => covered.Add(boards[item.ColumnId])))
            {
                await columnStatuses.InsertAsync(new BoardColumnStatus(guids.Create(), item.ColumnId, replacement));
            }
        }

        await columnStatuses.DeleteManyAsync(mapped, autoSave: true);
    }
}

/// <summary>Spaces created before boards existed get their default board.</summary>
public class AgileDataSeedContributor(BoardProvider provider, IProjectAccess projectAccess)
    : IDataSeedContributor, ITransientDependency
{
    public async Task SeedAsync(DataSeedContext context)
    {
        foreach (var project in await projectAccess.GetAllRefsAsync())
        {
            await provider.CreateDefaultAsync(project.Id);
        }
    }
}
