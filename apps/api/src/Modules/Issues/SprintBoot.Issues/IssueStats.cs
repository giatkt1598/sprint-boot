using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Issues;

public class IssueProjectStats(IRepository<Issue, Guid> issues) : IIssueProjectStats, ITransientDependency
{
    public async Task<Dictionary<Guid, ProjectIssueStats>> GetAsync(IEnumerable<Guid> projectIds)
    {
        var ids = projectIds.ToList();
        var query = await issues.GetQueryableAsync();
        var rows = await query.Where(issue => ids.Contains(issue.ProjectId))
            .GroupBy(issue => issue.ProjectId)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                UpdatedAt = group.Max(issue => issue.LastModificationTime ?? issue.CreationTime),
            })
            .ToListAsync();
        return rows.ToDictionary(row => row.Key, row => new ProjectIssueStats(row.Count, row.UpdatedAt));
    }
}
