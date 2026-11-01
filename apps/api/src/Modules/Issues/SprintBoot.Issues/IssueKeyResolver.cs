using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Issues;

public class IssueKeyResolver(IRepository<Issue, Guid> issues, IProjectAccess projects) : IIssueKeyResolver, ITransientDependency
{
    public async Task<Dictionary<string, IssueRef>> ResolveAsync(IReadOnlyCollection<string> keys)
    {
        var result = new Dictionary<string, IssueRef>();
        foreach (var key in keys)
        {
            var separator = key.LastIndexOf('-');
            if (separator <= 0 || !int.TryParse(key[(separator + 1)..], out var number))
            {
                continue;
            }

            var project = await projects.FindRefByKeyAsync(key[..separator]);
            var issue = project is null ? null : await issues.FirstOrDefaultAsync(item => item.ProjectId == project.Id && item.Number == number);
            if (issue is not null)
            {
                result[key] = new IssueRef(issue.Id, issue.ProjectId);
            }
        }

        return result;
    }
}
