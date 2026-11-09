using SprintBoot.Projects;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Guids;

namespace SprintBoot.Tickets;

public class TicketsDataSeedContributor(IRepository<Priority, Guid> priorities, IRepository<Ticket, Guid> tickets, IGuidGenerator guids)
    : IDataSeedContributor, ITransientDependency
{
    public const string DefaultPriority = "Medium";

    public async Task SeedAsync(DataSeedContext context)
    {
        await BackfillRanksAsync();

        if (await priorities.AnyAsync())
        {
            return;
        }

        string[] names = ["Highest", "High", DefaultPriority, "Low", "Lowest"];
        await priorities.InsertManyAsync(names.Select((name, order) => new Priority(guids.Create(), name, order)));
    }

    /// <summary>Tickets created before ranks existed keep their creation order.</summary>
    private async Task BackfillRanksAsync()
    {
        var unranked = await tickets.GetListAsync(ticket => ticket.Rank == string.Empty);
        foreach (var group in unranked.GroupBy(ticket => ticket.ProjectId))
        {
            var ordered = group.OrderBy(ticket => ticket.Number).ToList();
            var ranks = RankMath.Sequence(ordered.Count);
            for (var index = 0; index < ordered.Count; index++)
            {
                ordered[index].SetRank(ranks[index]);
            }

            await tickets.UpdateManyAsync(ordered, autoSave: true);
        }
    }
}

public class DefaultTicketTypesHandler(IRepository<TicketType, Guid> types, IGuidGenerator guids)
    : ILocalEventHandler<ProjectCreatedEvent>, ITransientDependency
{
    public async Task HandleEventAsync(ProjectCreatedEvent eventData)
    {
        await types.InsertManyAsync(
        [
            new TicketType(guids.Create(), eventData.ProjectId, "Epic", "epic", TicketHierarchy.Epic),
            new TicketType(guids.Create(), eventData.ProjectId, "Story", "story", TicketHierarchy.Standard),
            new TicketType(guids.Create(), eventData.ProjectId, "Task", "task", TicketHierarchy.Standard),
            new TicketType(guids.Create(), eventData.ProjectId, "Bug", "bug", TicketHierarchy.Standard),
            new TicketType(guids.Create(), eventData.ProjectId, "Subtask", "subtask", TicketHierarchy.Subtask),
        ]);
    }
}
