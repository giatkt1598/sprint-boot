using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Users;

namespace SprintBoot.Issues;

public class WatcherStore(IRepository<IssueWatcher, Guid> watchers, IGuidGenerator guids) : ITransientDependency
{
    public async Task EnsureAsync(Guid issueId, params Guid?[] userIds)
    {
        foreach (var userId in userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct())
        {
            if (!await watchers.AnyAsync(item => item.IssueId == issueId && item.UserId == userId))
            {
                await watchers.InsertAsync(new IssueWatcher(guids.Create(), issueId, userId), autoSave: true);
            }
        }
    }
}

[Authorize]
public class WatcherAppService(
    IRepository<Issue, Guid> issues,
    IRepository<IssueWatcher, Guid> watchers,
    WatcherStore store,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory)
    : ApplicationService, IWatcherAppService
{
    public async Task<WatchersDto> GetAsync(Guid issueId)
    {
        await CheckAsync(issueId);
        return await ToDtoAsync(issueId);
    }

    public async Task<WatchersDto> WatchAsync(Guid issueId)
    {
        await CheckAsync(issueId);
        await store.EnsureAsync(issueId, CurrentUser.GetId());
        return await ToDtoAsync(issueId);
    }

    public async Task<WatchersDto> UnwatchAsync(Guid issueId)
    {
        await CheckAsync(issueId);
        var userId = CurrentUser.GetId();
        await watchers.DeleteAsync(item => item.IssueId == issueId && item.UserId == userId, autoSave: true);
        return await ToDtoAsync(issueId);
    }

    private async Task CheckAsync(Guid issueId)
    {
        var issue = await issues.GetAsync(issueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);
    }

    private async Task<WatchersDto> ToDtoAsync(Guid issueId)
    {
        var ids = (await watchers.GetListAsync(item => item.IssueId == issueId)).Select(item => item.UserId).ToList();
        var users = await userDirectory.GetByIdsAsync(ids);
        return new WatchersDto
        {
            Count = ids.Count,
            WatchedByMe = ids.Contains(CurrentUser.GetId()),
            Users = [.. users.Values.OrderBy(user => user.Name)],
        };
    }
}

public class IssueFactsProvider(IRepository<Issue, Guid> issues, IRepository<IssueWatcher, Guid> watchers, IProjectAccess projectAccess)
    : IIssueFactsProvider, ITransientDependency
{
    public async Task<IssueFacts?> FindAsync(Guid issueId)
    {
        var issue = await issues.FindAsync(issueId);
        if (issue is null)
        {
            return null;
        }

        var project = await projectAccess.GetRefAsync(issue.ProjectId);
        var watching = await watchers.GetListAsync(item => item.IssueId == issueId);
        return new IssueFacts(
            issue.Id, $"{project.Key}-{issue.Number}", issue.Title, issue.AssigneeId, issue.ReporterId,
            [.. watching.Select(item => item.UserId)]);
    }
}
