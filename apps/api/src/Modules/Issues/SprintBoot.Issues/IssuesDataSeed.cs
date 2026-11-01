using SprintBoot.Projects;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Guids;

namespace SprintBoot.Issues;

public class IssuesDataSeedContributor(IRepository<Priority, Guid> priorities, IRepository<Issue, Guid> issues, IGuidGenerator guids)
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

    /// <summary>Issues created before ranks existed keep their creation order.</summary>
    private async Task BackfillRanksAsync()
    {
        var unranked = await issues.GetListAsync(issue => issue.Rank == string.Empty);
        foreach (var group in unranked.GroupBy(issue => issue.ProjectId))
        {
            var ordered = group.OrderBy(issue => issue.Number).ToList();
            var ranks = RankMath.Sequence(ordered.Count);
            for (var index = 0; index < ordered.Count; index++)
            {
                ordered[index].SetRank(ranks[index]);
            }

            await issues.UpdateManyAsync(ordered, autoSave: true);
        }
    }
}

public class DefaultIssueTypesHandler(IRepository<IssueType, Guid> types, IGuidGenerator guids)
    : ILocalEventHandler<ProjectCreatedEvent>, ITransientDependency
{
    public async Task HandleEventAsync(ProjectCreatedEvent eventData)
    {
        await types.InsertManyAsync(
        [
            new IssueType(guids.Create(), eventData.ProjectId, "Epic", "epic", IssueHierarchy.Epic),
            new IssueType(guids.Create(), eventData.ProjectId, "Story", "story", IssueHierarchy.Standard),
            new IssueType(guids.Create(), eventData.ProjectId, "Task", "task", IssueHierarchy.Standard),
            new IssueType(guids.Create(), eventData.ProjectId, "Bug", "bug", IssueHierarchy.Standard),
            new IssueType(guids.Create(), eventData.ProjectId, "Subtask", "subtask", IssueHierarchy.Subtask),
        ]);
    }
}
