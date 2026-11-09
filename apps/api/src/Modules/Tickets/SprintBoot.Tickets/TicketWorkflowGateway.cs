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

namespace SprintBoot.Tickets;

[ExposeServices(typeof(IWorkflowTicketGateway))]
public class TicketWorkflowGateway(
    IRepository<Ticket, Guid> tickets,
    IRepository<TicketLabel, Guid> ticketLabels,
    IRepository<TicketActivity, Guid> activities,
    IRepository<Comment, Guid> comments,
    IProjectAccess projectAccess,
    ICurrentActor currentActor,
    IClock clock,
    IGuidGenerator guids,
    IDataFilter dataFilter,
    IDistributedEventBus eventBus,
    RichTextSanitizer sanitizer,
    TicketAppService ticketService)
    : IWorkflowTicketGateway, ITransientDependency
{
    public async Task<TransitionSubject> GetSubjectAsync(Guid ticketId)
    {
        var ticket = await tickets.GetAsync(ticketId);
        var project = await projectAccess.GetRefAsync(ticket.ProjectId);
        var hasLabels = await ticketLabels.AnyAsync(link => link.TicketId == ticketId);

        return new TransitionSubject(
            ticket.Id,
            $"{project.Key}-{ticket.Number}",
            ticket.ProjectId,
            ticket.TypeId,
            ticket.StatusId,
            ticket.WorkflowVersionId,
            ticket.AssigneeId,
            ticket.ReporterId,
            ticket.ConcurrencyStamp,
            new Dictionary<string, bool>
            {
                [TicketFieldKeys.Title] = true,
                [TicketFieldKeys.Description] = ticket.DescriptionText.Length > 0,
                [TicketFieldKeys.Assignee] = ticket.AssigneeId.HasValue,
                [TicketFieldKeys.Reporter] = ticket.ReporterId.HasValue,
                [TicketFieldKeys.Parent] = ticket.ParentId.HasValue,
                [TicketFieldKeys.Labels] = hasLabels,
                [TicketFieldKeys.StoryPoints] = ticket.StoryPoints.HasValue,
                [TicketFieldKeys.StartDate] = ticket.StartDate.HasValue,
                [TicketFieldKeys.DueDate] = ticket.DueDate.HasValue,
            });
    }

    public async Task<List<Guid>> GetSubtaskStatusIdsAsync(Guid ticketId)
    {
        var children = await tickets.GetListAsync(child => child.ParentId == ticketId);
        return [.. children.Select(child => child.StatusId)];
    }

    public async Task MoveAsync(Guid ticketId, Guid statusId, string fromName, string toName)
    {
        var ticket = await tickets.GetAsync(ticketId);
        ticket.MoveTo(statusId);
        await tickets.UpdateAsync(ticket, autoSave: true);
        await activities.InsertAsync(new TicketActivity(
            guids.Create(), ticket.Id, currentActor.Actor, TicketFieldKeys.Status, fromName, toName, clock.Now));
    }

    public async Task SetFieldAsync(Guid ticketId, string field, JsonElement value) =>
        await ticketService.SetFieldForWorkflowAsync(await tickets.GetAsync(ticketId), field, value);

    public async Task SetResolutionAsync(Guid ticketId, string? resolution)
    {
        var ticket = await tickets.GetAsync(ticketId);
        ticket.SetResolution(resolution);
        await tickets.UpdateAsync(ticket, autoSave: true);
    }

    public async Task AddCommentAsync(Guid ticketId, string body)
    {
        var ticket = await tickets.GetAsync(ticketId);
        var (html, text) = sanitizer.Clean(body);
        var comment = new Comment(guids.Create(), ticketId, null, currentActor.Actor.Id, html, text);
        await comments.InsertAsync(comment, autoSave: true);
        await eventBus.PublishAsync(
            new CommentAddedEto
            {
                EventId = guids.Create(),
                CommentId = comment.Id,
                TicketId = ticketId,
                ProjectId = ticket.ProjectId,
                ActorId = currentActor.Actor.Id,
                Mentioned = RichTextSanitizer.Mentions(html),
            },
            useOutbox: true);
    }

    public async Task<Dictionary<Guid, int>> CountByStatusAsync(TicketScope? scope = null)
    {
        var query = scope is null ? await tickets.GetQueryableAsync() : await InScopeAsync(scope);
        return await query.GroupBy(ticket => ticket.StatusId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count);
    }

    public async Task<int> CountPendingAsync(TicketScope scope, Guid toVersionId)
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            return await (await InScopeAsync(scope)).CountAsync(ticket => ticket.WorkflowVersionId != toVersionId);
        }
    }

    public async Task<int> CountWithStatusAsync(Guid statusId)
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            return await tickets.CountAsync(ticket => ticket.StatusId == statusId);
        }
    }

    public async Task ReplaceStatusAsync(Guid statusId, Guid replacementId)
    {
        // Soft-deleted tickets move too, so restoring one can never leave it on a dropped status.
        using (dataFilter.Disable<ISoftDelete>())
        {
            await (await tickets.GetQueryableAsync())
                .Where(ticket => ticket.StatusId == statusId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(ticket => ticket.StatusId, replacementId));
        }
    }

    public async Task<int> MigrateBatchAsync(
        TicketScope scope, Guid toVersionId, IReadOnlyDictionary<Guid, Guid> statusMappings, int batchSize)
    {
        // Soft-deleted tickets move too, so restoring one can never leave it on a dropped status.
        using (dataFilter.Disable<ISoftDelete>())
        {
            var batch = await (await InScopeAsync(scope))
                .Where(ticket => ticket.WorkflowVersionId == null || ticket.WorkflowVersionId != toVersionId)
                .OrderBy(ticket => ticket.Id)
                .Take(batchSize)
                .ToListAsync();

            foreach (var ticket in batch)
            {
                if (statusMappings.TryGetValue(ticket.StatusId, out var target))
                {
                    ticket.MoveTo(target);
                }

                ticket.SetWorkflowVersion(toVersionId);
            }

            await tickets.UpdateManyAsync(batch, autoSave: true);
            return batch.Count;
        }
    }

    private async Task<IQueryable<Ticket>> InScopeAsync(TicketScope scope)
    {
        var query = await tickets.GetQueryableAsync();
        var versions = scope.VersionIds;
        query = versions.Length == 0
            ? query.Where(ticket => ticket.WorkflowVersionId == null)
            : query.Where(ticket => ticket.WorkflowVersionId == null || versions.Contains(ticket.WorkflowVersionId.Value));

        if (scope.ProjectId is { } projectId)
        {
            query = query.Where(ticket => ticket.ProjectId == projectId);
        }

        if (scope.TicketTypeId is { } typeId)
        {
            query = query.Where(ticket => ticket.TypeId == typeId);
        }

        return query;
    }
}
