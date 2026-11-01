using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace SprintBoot.Issues;

[ExposeServices(typeof(IWorkflowIssueGateway))]
public class IssueWorkflowGateway(
    IRepository<Issue, Guid> issues,
    IRepository<IssueLabel, Guid> issueLabels,
    IRepository<IssueActivity, Guid> activities,
    IRepository<Comment, Guid> comments,
    IProjectAccess projectAccess,
    ICurrentActor currentActor,
    IClock clock,
    IGuidGenerator guids,
    IDataFilter dataFilter,
    IDistributedEventBus eventBus,
    RichTextSanitizer sanitizer,
    IssueAppService issueService)
    : IWorkflowIssueGateway, ITransientDependency
{
    public async Task<TransitionSubject> GetSubjectAsync(Guid issueId)
    {
        var issue = await issues.GetAsync(issueId);
        var project = await projectAccess.GetRefAsync(issue.ProjectId);
        var hasLabels = await issueLabels.AnyAsync(link => link.IssueId == issueId);

        return new TransitionSubject(
            issue.Id,
            $"{project.Key}-{issue.Number}",
            issue.ProjectId,
            issue.TypeId,
            issue.StatusId,
            issue.WorkflowVersionId,
            issue.AssigneeId,
            issue.ReporterId,
            issue.ConcurrencyStamp,
            new Dictionary<string, bool>
            {
                [IssueFieldKeys.Title] = true,
                [IssueFieldKeys.Description] = issue.DescriptionText.Length > 0,
                [IssueFieldKeys.Assignee] = issue.AssigneeId.HasValue,
                [IssueFieldKeys.Reporter] = issue.ReporterId.HasValue,
                [IssueFieldKeys.Parent] = issue.ParentId.HasValue,
                [IssueFieldKeys.Labels] = hasLabels,
                [IssueFieldKeys.StoryPoints] = issue.StoryPoints.HasValue,
                [IssueFieldKeys.StartDate] = issue.StartDate.HasValue,
                [IssueFieldKeys.DueDate] = issue.DueDate.HasValue,
            });
    }

    public async Task<List<Guid>> GetSubtaskStatusIdsAsync(Guid issueId)
    {
        var children = await issues.GetListAsync(child => child.ParentId == issueId);
        return [.. children.Select(child => child.StatusId)];
    }

    public async Task MoveAsync(Guid issueId, Guid statusId, string fromName, string toName)
    {
        var issue = await issues.GetAsync(issueId);
        issue.MoveTo(statusId);
        await issues.UpdateAsync(issue, autoSave: true);
        await activities.InsertAsync(new IssueActivity(
            guids.Create(), issue.Id, currentActor.Actor, IssueFieldKeys.Status, fromName, toName, clock.Now));
    }

    public async Task SetFieldAsync(Guid issueId, string field, JsonElement value) =>
        await issueService.SetFieldForWorkflowAsync(await issues.GetAsync(issueId), field, value);

    public async Task SetResolutionAsync(Guid issueId, string? resolution)
    {
        var issue = await issues.GetAsync(issueId);
        issue.SetResolution(resolution);
        await issues.UpdateAsync(issue, autoSave: true);
    }

    public async Task AddCommentAsync(Guid issueId, string body)
    {
        var issue = await issues.GetAsync(issueId);
        var (html, text) = sanitizer.Clean(body);
        var comment = new Comment(guids.Create(), issueId, null, currentActor.Actor.Id, html, text);
        await comments.InsertAsync(comment, autoSave: true);
        await eventBus.PublishAsync(
            new CommentAddedEto { CommentId = comment.Id, IssueId = issueId, ProjectId = issue.ProjectId }, useOutbox: true);
    }

    public async Task<Dictionary<Guid, int>> CountByStatusAsync(IssueScope? scope = null)
    {
        var query = scope is null ? await issues.GetQueryableAsync() : await InScopeAsync(scope);
        return await query.GroupBy(issue => issue.StatusId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count);
    }

    public async Task<int> CountPendingAsync(IssueScope scope, Guid toVersionId)
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            return await (await InScopeAsync(scope)).CountAsync(issue => issue.WorkflowVersionId != toVersionId);
        }
    }

    public async Task<int> MigrateBatchAsync(
        IssueScope scope, Guid toVersionId, IReadOnlyDictionary<Guid, Guid> statusMappings, int batchSize)
    {
        // Soft-deleted issues move too, so restoring one can never leave it on a dropped status.
        using (dataFilter.Disable<ISoftDelete>())
        {
            var batch = await (await InScopeAsync(scope))
                .Where(issue => issue.WorkflowVersionId == null || issue.WorkflowVersionId != toVersionId)
                .OrderBy(issue => issue.Id)
                .Take(batchSize)
                .ToListAsync();

            foreach (var issue in batch)
            {
                if (statusMappings.TryGetValue(issue.StatusId, out var target))
                {
                    issue.MoveTo(target);
                }

                issue.SetWorkflowVersion(toVersionId);
            }

            await issues.UpdateManyAsync(batch, autoSave: true);
            return batch.Count;
        }
    }

    private async Task<IQueryable<Issue>> InScopeAsync(IssueScope scope)
    {
        var query = await issues.GetQueryableAsync();
        var versions = scope.VersionIds;
        query = versions.Length == 0
            ? query.Where(issue => issue.WorkflowVersionId == null)
            : query.Where(issue => issue.WorkflowVersionId == null || versions.Contains(issue.WorkflowVersionId.Value));

        if (scope.ProjectId is { } projectId)
        {
            query = query.Where(issue => issue.ProjectId == projectId);
        }

        if (scope.IssueTypeId is { } typeId)
        {
            query = query.Where(issue => issue.TypeId == typeId);
        }

        return query;
    }
}
