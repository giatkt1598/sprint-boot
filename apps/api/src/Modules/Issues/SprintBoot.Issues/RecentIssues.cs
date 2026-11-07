using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Projects;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace SprintBoot.Issues;

/// <summary>One person's last visit to one issue. There is a row per pair, moved to the top by each new visit.</summary>
public class RecentIssue : Entity<Guid>
{
    public Guid UserId { get; private set; }

    public Guid IssueId { get; private set; }

    public DateTime ViewedAt { get; set; }

    protected RecentIssue()
    {
    }

    public RecentIssue(Guid id, Guid userId, Guid issueId, DateTime viewedAt)
        : base(id)
    {
        UserId = userId;
        IssueId = issueId;
        ViewedAt = viewedAt;
    }
}

[Authorize]
public class RecentIssueAppService(
    IRepository<RecentIssue, Guid> recents,
    IRepository<Issue, Guid> issues,
    IProjectAccess projectAccess,
    IssueMapper mapper,
    IClock clock)
    : ApplicationService, IRecentIssueAppService
{
    /// <summary>What the list shows.</summary>
    private const int ListSize = 20;

    /// <summary>What is kept per person; the extra covers issues that drop out of the list because access ended.</summary>
    private const int KeptPerUser = 50;

    public async Task VisitAsync(Guid issueId)
    {
        var issue = await issues.FindAsync(issueId) ?? throw new EntityNotFoundException(typeof(Issue), issueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);

        var userId = CurrentUser.GetId();
        var row = await recents.FirstOrDefaultAsync(item => item.UserId == userId && item.IssueId == issueId);
        if (row is null)
        {
            await recents.InsertAsync(new RecentIssue(GuidGenerator.Create(), userId, issueId, clock.Now), autoSave: true);
        }
        else
        {
            row.ViewedAt = clock.Now;
            await recents.UpdateAsync(row, autoSave: true);
        }

        var surplus = (await recents.GetListAsync(item => item.UserId == userId))
            .OrderByDescending(item => item.ViewedAt)
            .Skip(KeptPerUser)
            .ToList();
        if (surplus.Count > 0)
        {
            await recents.DeleteManyAsync(surplus, autoSave: true);
        }
    }

    public async Task<List<IssueSummaryDto>> GetListAsync()
    {
        var userId = CurrentUser.GetId();
        var rows = (await recents.GetListAsync(item => item.UserId == userId))
            .OrderByDescending(item => item.ViewedAt)
            .Take(KeptPerUser)
            .ToList();
        var ids = rows.Select(row => row.IssueId).ToList();

        // Spaces the person has left, and issues deleted since, simply drop out.
        var visible = await projectAccess.GetVisibleProjectIdsAsync();
        var query = (await issues.GetQueryableAsync()).Where(issue => ids.Contains(issue.Id));
        if (visible is not null)
        {
            query = query.Where(issue => visible.Contains(issue.ProjectId));
        }

        var found = await query.ToListAsync();
        var summaries = (await mapper.ToSummariesAsync(found)).ToDictionary(summary => summary.Id);
        return [.. rows.Where(row => summaries.ContainsKey(row.IssueId)).Select(row => summaries[row.IssueId]).Take(ListSize)];
    }
}
