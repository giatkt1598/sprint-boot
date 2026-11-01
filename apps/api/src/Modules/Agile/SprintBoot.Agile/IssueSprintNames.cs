using SprintBoot.Issues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Agile;

/// <summary>An open sprint wins over a closed one, then the newest, as on the issue page.</summary>
public class IssueSprintNames(
    IRepository<SprintIssue, Guid> sprintIssues,
    IRepository<Sprint, Guid> sprints) : IIssueSprintNames, ITransientDependency
{
    public async Task<Dictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> issueIds)
    {
        var ids = issueIds.ToList();
        var rows = await sprintIssues.GetListAsync(item => ids.Contains(item.IssueId) && item.RemovedAt == null);
        var sprintIds = rows.Select(row => row.SprintId).Distinct().ToList();
        var known = (await sprints.GetListAsync(item => sprintIds.Contains(item.Id))).ToDictionary(item => item.Id);

        return rows
            .GroupBy(row => row.IssueId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => known[row.SprintId])
                    .OrderBy(item => item.State == SprintState.Closed)
                    .ThenByDescending(item => item.CreationTime)
                    .First().Name);
    }
}
