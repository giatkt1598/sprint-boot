using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace SprintBoot.Issues;

[Authorize]
public class CommentAppService(
    IRepository<Issue, Guid> issues,
    IRepository<Comment, Guid> comments,
    IRepository<CommentReaction, Guid> reactions,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    IDistributedEventBus eventBus,
    IClock clock,
    RichTextSanitizer sanitizer)
    : ApplicationService, ICommentAppService
{
    public async Task<List<CommentDto>> GetListAsync(Guid issueId)
    {
        var issue = await issues.GetAsync(issueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);

        var list = await comments.GetListAsync(comment => comment.IssueId == issueId);
        return await ToDtosAsync([.. list.OrderBy(comment => comment.CreationTime)]);
    }

    public async Task<CommentDto> CreateAsync(CreateCommentInput input)
    {
        var issue = await issues.GetAsync(input.IssueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.EditIssues);

        if (input.ParentId is { } parentId)
        {
            var parent = await comments.GetAsync(parentId);
            if (parent.IssueId != issue.Id || parent.ParentId is not null)
            {
                throw new BusinessException(IssueErrors.ReplyDepth);
            }
        }

        var (html, text) = Clean(input.Body);
        var comment = new Comment(GuidGenerator.Create(), issue.Id, input.ParentId, CurrentUser.GetId(), html, text);
        await comments.InsertAsync(comment, autoSave: true);
        await eventBus.PublishAsync(
            new CommentAddedEto { CommentId = comment.Id, IssueId = issue.Id, ProjectId = issue.ProjectId }, useOutbox: true);
        return (await ToDtosAsync([comment])).Single();
    }

    public async Task<CommentDto> UpdateAsync(Guid id, UpdateCommentInput input)
    {
        var comment = await comments.GetAsync(id);
        await CheckProjectAsync(comment, ProjectPermissions.ViewIssues);
        if (comment.AuthorId != CurrentUser.GetId())
        {
            throw new AbpAuthorizationException("Only the author can edit a comment");
        }

        var (html, text) = Clean(input.Body);
        comment.Edit(html, text, clock.Now);
        await comments.UpdateAsync(comment, autoSave: true);
        return (await ToDtosAsync([comment])).Single();
    }

    public async Task DeleteAsync(Guid id)
    {
        var comment = await comments.GetAsync(id);
        var issue = await CheckProjectAsync(comment, ProjectPermissions.ViewIssues);
        var isAuthor = comment.AuthorId == CurrentUser.GetId();
        if (!isAuthor && !await projectAccess.IsGrantedAsync(issue.ProjectId, ProjectPermissions.DeleteAnyComment))
        {
            throw new AbpAuthorizationException("Missing permission to delete this comment");
        }

        await comments.DeleteAsync(reply => reply.ParentId == id);
        await comments.DeleteAsync(comment);
    }

    public async Task<CommentDto> ToggleReactionAsync(Guid id, ToggleReactionInput input)
    {
        var comment = await comments.GetAsync(id);
        await CheckProjectAsync(comment, ProjectPermissions.EditIssues);

        var userId = CurrentUser.GetId();
        var existing = await reactions.FirstOrDefaultAsync(item =>
            item.CommentId == id && item.UserId == userId && item.Emoji == input.Emoji);
        if (existing is null)
        {
            await reactions.InsertAsync(new CommentReaction(GuidGenerator.Create(), id, userId, input.Emoji), autoSave: true);
        }
        else
        {
            await reactions.DeleteAsync(existing, autoSave: true);
        }

        return (await ToDtosAsync([comment])).Single();
    }

    private (string Html, string Text) Clean(string body)
    {
        var result = sanitizer.Clean(body);
        return string.IsNullOrEmpty(result.Text) ? throw new BusinessException(IssueErrors.CommentEmpty) : result;
    }

    private async Task<Issue> CheckProjectAsync(Comment comment, string permission)
    {
        var issue = await issues.GetAsync(comment.IssueId);
        await projectAccess.CheckAsync(issue.ProjectId, permission);
        return issue;
    }

    private async Task<List<CommentDto>> ToDtosAsync(List<Comment> list)
    {
        var ids = list.Select(comment => comment.Id).ToList();
        var allReactions = await reactions.GetListAsync(reaction => ids.Contains(reaction.CommentId));
        var users = await userDirectory.GetByIdsAsync(list.Select(comment => comment.AuthorId));
        var me = CurrentUser.GetId();

        return [.. list.Select(comment => new CommentDto
        {
            Id = comment.Id,
            ParentId = comment.ParentId,
            Author = users.GetValueOrDefault(comment.AuthorId),
            Body = comment.Body,
            CreatedAt = comment.CreationTime,
            IsEdited = comment.EditedAt.HasValue,
            Reactions = [.. allReactions.Where(reaction => reaction.CommentId == comment.Id)
                .GroupBy(reaction => reaction.Emoji)
                .Select(group => new CommentReactionDto
                {
                    Emoji = group.Key,
                    Count = group.Count(),
                    ReactedByMe = group.Any(reaction => reaction.UserId == me),
                })],
        })];
    }
}
