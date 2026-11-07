using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace SprintBoot.Issues;

/// <summary>The rich text page that introduces a space; one per space, made on the first save.</summary>
public class ProjectIntroduction : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public string Content { get; set; } = string.Empty;

    public Guid UpdatedById { get; set; }

    public DateTime UpdatedAt { get; set; }

    protected ProjectIntroduction()
    {
    }

    public ProjectIntroduction(Guid id, Guid projectId)
        : base(id)
    {
        ProjectId = projectId;
    }
}

[Authorize]
public class ProjectIntroductionAppService(
    IRepository<ProjectIntroduction, Guid> introductions,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    RichTextSanitizer sanitizer,
    RichTextFileStore richTextFiles,
    IClock clock)
    : ApplicationService, IProjectIntroductionAppService
{
    public async Task<ProjectIntroductionDto> GetAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        return await ToDtoAsync(projectId, await introductions.FirstOrDefaultAsync(item => item.ProjectId == projectId));
    }

    public async Task<ProjectIntroductionDto> UpdateAsync(Guid projectId, UpdateProjectIntroductionInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);
        var introduction = await introductions.FirstOrDefaultAsync(item => item.ProjectId == projectId);
        var isNew = introduction is null;
        introduction ??= new ProjectIntroduction(GuidGenerator.Create(), projectId);
        introduction.Content = sanitizer.Clean(input.Content).Html;
        introduction.UpdatedById = CurrentUser.GetId();
        introduction.UpdatedAt = clock.Now;
        if (isNew)
        {
            await introductions.InsertAsync(introduction, autoSave: true);
        }
        else
        {
            await introductions.UpdateAsync(introduction, autoSave: true);
        }

        await richTextFiles.SyncAsync(projectId, introduction.Id, introduction.Content);
        return await ToDtoAsync(projectId, introduction);
    }

    private async Task<ProjectIntroductionDto> ToDtoAsync(Guid projectId, ProjectIntroduction? introduction)
    {
        var editor = introduction is null ? null : (await userDirectory.GetByIdsAsync([introduction.UpdatedById])).GetValueOrDefault(introduction.UpdatedById);
        return new ProjectIntroductionDto
        {
            Content = richTextFiles.Sign(introduction?.Content),
            UpdatedBy = editor,
            UpdatedAt = introduction?.UpdatedAt,
            CanEdit = await projectAccess.IsGrantedAsync(projectId, ProjectPermissions.Manage),
        };
    }
}

public class IssueOverviewFacts(
    IRepository<Issue, Guid> issues,
    IRepository<IssueType, Guid> types,
    IRepository<IssueActivity, Guid> activities,
    IRepository<Comment, Guid> comments,
    IStatusLookup statusLookup,
    IProjectAccess projectAccess) : IIssueOverviewFacts, ITransientDependency
{
    public async Task<List<IssueOverviewRow>> GetIssuesAsync(Guid projectId)
    {
        var issueQuery = await issues.GetQueryableAsync();
        var rows = await issueQuery.Where(issue => issue.ProjectId == projectId)
            .Select(issue => new
            {
                issue.Id,
                issue.ParentId,
                issue.TypeId,
                issue.StatusId,
                issue.AssigneeId,
                issue.StoryPoints,
                issue.CreationTime,
                UpdatedAt = issue.LastModificationTime ?? issue.CreationTime,
            })
            .ToListAsync();

        var ids = rows.Select(row => row.Id).ToList();
        var activityQuery = await activities.GetQueryableAsync();
        var lastMoves = await activityQuery.Where(item => item.Field == IssueFieldKeys.Status && ids.Contains(item.IssueId))
            .GroupBy(item => item.IssueId)
            .Select(group => new { group.Key, At = group.Max(item => item.CreatedAt) })
            .ToDictionaryAsync(item => item.Key, item => item.At);

        var typeMap = (await types.GetListAsync(type => type.ProjectId == projectId)).ToDictionary(type => type.Id);
        var statuses = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);

        return [.. rows.Select(row =>
        {
            var type = typeMap.GetValueOrDefault(row.TypeId);
            var status = statuses.GetValueOrDefault(row.StatusId);
            var category = status?.Category ?? StatusCategory.ToDo;
            return new IssueOverviewRow(
                row.Id,
                row.ParentId,
                type?.Name ?? string.Empty,
                type?.Icon ?? string.Empty,
                type?.Hierarchy ?? IssueHierarchy.Standard,
                category,
                status?.Name ?? string.Empty,
                row.AssigneeId,
                row.StoryPoints,
                row.CreationTime,
                row.UpdatedAt,
                category == StatusCategory.Done ? lastMoves.GetValueOrDefault(row.Id, row.UpdatedAt) : null);
        })];
    }

    public async Task<List<IssueEventRow>> GetRecentEventsAsync(Guid projectId, int take)
    {
        var issueQuery = (await issues.GetQueryableAsync()).Where(issue => issue.ProjectId == projectId);

        var created = await issueQuery.Where(issue => issue.CreatorId != null)
            .OrderByDescending(issue => issue.CreationTime)
            .Take(take)
            .Select(issue => new { ActorId = issue.CreatorId!.Value, IssueId = issue.Id, Field = (string?)null, Value = (string?)null, At = issue.CreationTime, Kind = IssueEventKind.Created })
            .ToListAsync();

        var changed = await (from activity in await activities.GetQueryableAsync()
                             join issue in issueQuery on activity.IssueId equals issue.Id
                             orderby activity.CreatedAt descending
                             select new { activity.ActorId, IssueId = issue.Id, Field = (string?)activity.Field, Value = activity.Field == IssueFieldKeys.Status || activity.Field == IssueFieldKeys.WorkLog ? activity.NewValue : null, At = activity.CreatedAt, Kind = IssueEventKind.Changed })
            .Take(take)
            .ToListAsync();

        var commented = await (from comment in await comments.GetQueryableAsync()
                               join issue in issueQuery on comment.IssueId equals issue.Id
                               orderby comment.CreationTime descending
                               select new { ActorId = comment.AuthorId, IssueId = issue.Id, Field = (string?)null, Value = (string?)null, At = comment.CreationTime, Kind = IssueEventKind.Commented })
            .Take(take)
            .ToListAsync();

        var latest = created.Concat(changed).Concat(commented).OrderByDescending(item => item.At).Take(take).ToList();
        var issueIds = latest.Select(item => item.IssueId).Distinct().ToList();
        var details = await issueQuery.Where(issue => issueIds.Contains(issue.Id))
            .Select(issue => new { issue.Id, issue.Number, issue.Title })
            .ToDictionaryAsync(issue => issue.Id);
        var key = (await projectAccess.GetRefAsync(projectId)).Key;

        return [.. latest.Where(item => details.ContainsKey(item.IssueId)).Select(item =>
        {
            var issue = details[item.IssueId];
            return new IssueEventRow(item.ActorId, item.Kind, $"{key}-{issue.Number}", issue.Title, item.Field, item.Value, item.At);
        })];
    }
}
