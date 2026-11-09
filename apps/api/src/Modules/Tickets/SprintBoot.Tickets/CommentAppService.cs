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

namespace SprintBoot.Tickets;

[Authorize]
public class CommentAppService(
    IRepository<Ticket, Guid> tickets,
    IRepository<Comment, Guid> comments,
    IRepository<CommentReaction, Guid> reactions,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    IDistributedEventBus eventBus,
    IClock clock,
    WatcherStore watcherStore,
    RichTextSanitizer sanitizer,
    RichTextFileStore richTextFiles)
    : ApplicationService, ICommentAppService
{
    public async Task<List<CommentDto>> GetListAsync(Guid ticketId)
    {
        var ticket = await tickets.GetAsync(ticketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);

        var list = await comments.GetListAsync(comment => comment.TicketId == ticketId);
        return await ToDtosAsync([.. list.OrderBy(comment => comment.CreationTime)]);
    }

    public async Task<CommentDto> CreateAsync(CreateCommentInput input)
    {
        var ticket = await tickets.GetAsync(input.TicketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.EditTickets);

        Guid? parentAuthorId = null;
        if (input.ParentId is { } parentId)
        {
            var parent = await comments.GetAsync(parentId);
            if (parent.TicketId != ticket.Id || parent.ParentId is not null)
            {
                throw new BusinessException(TicketErrors.ReplyDepth);
            }

            parentAuthorId = parent.AuthorId;
        }

        var (html, text) = Clean(input.Body);
        var comment = new Comment(GuidGenerator.Create(), ticket.Id, input.ParentId, CurrentUser.GetId(), html, text);
        await comments.InsertAsync(comment, autoSave: true);
        await richTextFiles.SyncAsync(ticket.ProjectId, comment.Id, html);
        await watcherStore.EnsureAsync(ticket.Id, CurrentUser.GetId());
        await eventBus.PublishAsync(
            new CommentAddedEto
            {
                EventId = GuidGenerator.Create(),
                CommentId = comment.Id,
                TicketId = ticket.Id,
                ProjectId = ticket.ProjectId,
                ActorId = CurrentUser.GetId(),
                Mentioned = RichTextSanitizer.Mentions(html),
                ParentAuthorId = parentAuthorId,
            },
            useOutbox: true);
        return (await ToDtosAsync([comment])).Single();
    }

    public async Task<CommentDto> UpdateAsync(Guid id, UpdateCommentInput input)
    {
        var comment = await comments.GetAsync(id);
        var ticket = await CheckProjectAsync(comment, ProjectPermissions.ViewTickets);
        if (comment.AuthorId != CurrentUser.GetId())
        {
            throw new AbpAuthorizationException("Only the author can edit a comment");
        }

        var (html, text) = Clean(input.Body);
        comment.Edit(html, text, clock.Now);
        await comments.UpdateAsync(comment, autoSave: true);
        await richTextFiles.SyncAsync(ticket.ProjectId, comment.Id, html);
        return (await ToDtosAsync([comment])).Single();
    }

    public async Task DeleteAsync(Guid id)
    {
        var comment = await comments.GetAsync(id);
        var ticket = await CheckProjectAsync(comment, ProjectPermissions.ViewTickets);
        var isAuthor = comment.AuthorId == CurrentUser.GetId();
        if (!isAuthor && !await projectAccess.IsGrantedAsync(ticket.ProjectId, ProjectPermissions.DeleteAnyComment))
        {
            throw new AbpAuthorizationException("Missing permission to delete this comment");
        }

        var replies = await comments.GetListAsync(reply => reply.ParentId == id);
        await richTextFiles.RemoveOwnedByAsync([id, .. replies.Select(reply => reply.Id)]);
        await comments.DeleteAsync(reply => reply.ParentId == id);
        await comments.DeleteAsync(comment);
    }

    public async Task<CommentDto> ToggleReactionAsync(Guid id, ToggleReactionInput input)
    {
        var comment = await comments.GetAsync(id);
        await CheckProjectAsync(comment, ProjectPermissions.EditTickets);

        var userId = CurrentUser.GetId();
        var existing = await reactions.FirstOrDefaultAsync(item =>
            item.CommentId == id && item.UserId == userId && item.Emoji == input.Emoji);
        if (existing is null)
        {
            await reactions.InsertAsync(new CommentReaction(GuidGenerator.Create(), id, userId, input.Emoji, clock.Now), autoSave: true);
            await eventBus.PublishAsync(
                new CommentReactedEto
                {
                    EventId = ReactionEventId(id, userId, input.Emoji),
                    CommentId = id,
                    TicketId = comment.TicketId,
                    ProjectId = (await tickets.GetAsync(comment.TicketId)).ProjectId,
                    ActorId = userId,
                    CommentAuthorId = comment.AuthorId,
                    Emoji = input.Emoji,
                },
                useOutbox: true);
        }
        else
        {
            await reactions.DeleteAsync(existing, autoSave: true);
        }

        return (await ToDtosAsync([comment])).Single();
    }

    /// <summary>The same person reacting to the same comment with the same emoji always gives the same id, so taking it back and giving it again is one notification.</summary>
    private static Guid ReactionEventId(Guid commentId, Guid userId, string emoji) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"reaction:{commentId}:{userId}:{emoji}")));

    private (string Html, string Text) Clean(string body)
    {
        var result = sanitizer.Clean(body);
        var hasMedia = result.Html.Contains("<img", StringComparison.Ordinal) || result.Html.Contains("<video", StringComparison.Ordinal);
        return string.IsNullOrEmpty(result.Text) && !hasMedia ? throw new BusinessException(TicketErrors.CommentEmpty) : result;
    }

    private async Task<Ticket> CheckProjectAsync(Comment comment, string permission)
    {
        var ticket = await tickets.GetAsync(comment.TicketId);
        await projectAccess.CheckAsync(ticket.ProjectId, permission);
        return ticket;
    }

    private async Task<List<CommentDto>> ToDtosAsync(List<Comment> list)
    {
        var ids = list.Select(comment => comment.Id).ToList();
        var allReactions = await reactions.GetListAsync(reaction => ids.Contains(reaction.CommentId));
        var users = await userDirectory.GetByIdsAsync(list.Select(comment => comment.AuthorId).Concat(allReactions.Select(reaction => reaction.UserId)).Distinct());
        var me = CurrentUser.GetId();

        return [.. list.Select(comment => new CommentDto
        {
            Id = comment.Id,
            ParentId = comment.ParentId,
            Author = users.GetValueOrDefault(comment.AuthorId),
            Body = richTextFiles.Sign(comment.Body),
            CreatedAt = comment.CreationTime,
            IsEdited = comment.EditedAt.HasValue,
            Reactions = [.. allReactions.Where(reaction => reaction.CommentId == comment.Id)
                .GroupBy(reaction => reaction.Emoji)
                .Select(group => new CommentReactionDto
                {
                    Emoji = group.Key,
                    Count = group.Count(),
                    ReactedByMe = group.Any(reaction => reaction.UserId == me),
                    Users = [.. group
                        .OrderBy(reaction => reaction.CreatedAt)
                        .ThenBy(reaction => reaction.Id)
                        .Select(reaction => users.GetValueOrDefault(reaction.UserId))
                        .OfType<UserRefDto>()],
                })],
        })];
    }
}
