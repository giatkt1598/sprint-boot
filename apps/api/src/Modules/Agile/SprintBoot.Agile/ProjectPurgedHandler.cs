using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.Agile;

/// <summary>A purged space's boards and sprints go with it.</summary>
public class ProjectPurgedHandler(
    IRepository<Board, Guid> boards,
    IRepository<BoardColumn, Guid> columns,
    IRepository<BoardColumnStatus, Guid> columnStatuses,
    IRepository<BoardEntry, Guid> entries,
    IRepository<Sprint, Guid> sprints,
    IRepository<SprintTicket, Guid> sprintTickets)
    : IDistributedEventHandler<ProjectPurgedEto>, ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task HandleEventAsync(ProjectPurgedEto eventData)
    {
        var boardIds = (await boards.GetQueryableAsync()).Where(board => board.ProjectId == eventData.ProjectId).Select(board => board.Id);
        var sprintIds = (await sprints.GetQueryableAsync()).Where(sprint => boardIds.Contains(sprint.BoardId)).Select(sprint => sprint.Id);
        var columnIds = (await columns.GetQueryableAsync()).Where(column => boardIds.Contains(column.BoardId)).Select(column => column.Id);

        await sprintTickets.DeleteDirectAsync(item => sprintIds.Contains(item.SprintId));
        await sprints.DeleteDirectAsync(sprint => boardIds.Contains(sprint.BoardId));
        await columnStatuses.DeleteDirectAsync(item => columnIds.Contains(item.ColumnId));
        await columns.DeleteDirectAsync(column => boardIds.Contains(column.BoardId));
        await entries.DeleteDirectAsync(entry => boardIds.Contains(entry.BoardId));
        await boards.DeleteDirectAsync(board => board.ProjectId == eventData.ProjectId);
    }
}
