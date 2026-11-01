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
        foreach (var status in await statusLookup.GetListAsync())
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
