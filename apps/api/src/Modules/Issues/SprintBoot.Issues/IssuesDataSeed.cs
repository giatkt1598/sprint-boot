using SprintBoot.Projects;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Guids;

namespace SprintBoot.Issues;

public class IssuesDataSeedContributor(IRepository<Priority, Guid> priorities, IGuidGenerator guids)
    : IDataSeedContributor, ITransientDependency
{
    public const string DefaultPriority = "Medium";

    public async Task SeedAsync(DataSeedContext context)
    {
        if (await priorities.AnyAsync())
        {
            return;
        }

        string[] names = ["Highest", "High", DefaultPriority, "Low", "Lowest"];
        await priorities.InsertManyAsync(names.Select((name, order) => new Priority(guids.Create(), name, order)));
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
