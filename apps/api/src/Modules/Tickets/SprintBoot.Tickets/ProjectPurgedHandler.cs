using Microsoft.EntityFrameworkCore;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.Tickets;

/// <summary>A purged space takes its tickets with it: every row of this module for it, and the files it stored.</summary>
public class ProjectPurgedHandler(
    IRepository<Ticket, Guid> tickets,
    IRepository<TicketType, Guid> types,
    IRepository<TicketWatcher, Guid> watchers,
    IRepository<Label, Guid> labels,
    IRepository<TicketLabel, Guid> ticketLabels,
    IRepository<Comment, Guid> comments,
    IRepository<CommentReaction, Guid> reactions,
    IRepository<Attachment, Guid> attachments,
    IRepository<TicketActivity, Guid> activities,
    IRepository<WorkLog, Guid> workLogs,
    IRepository<TicketLink, Guid> links,
    IRepository<TicketTemplate, Guid> templates,
    IRepository<UploadSession, Guid> uploads,
    IRepository<ProjectIntroduction, Guid> introductions,
    IRepository<Goal, Guid> goals,
    IRepository<GoalUpdate, Guid> goalUpdates,
    IRepository<RecentTicket, Guid> recents,
    IRepository<RichTextFile, Guid> richTextFiles,
    IRepository<CustomField, Guid> customFields,
    IRepository<CustomFieldOption, Guid> customFieldOptions,
    IRepository<ProjectCustomField, Guid> spaceFields,
    IRepository<TicketFieldValue, Guid> fieldValues,
    IBlobContainer<AttachmentContainer> container,
    IUploadStorage uploadStorage,
    IDataFilter dataFilter)
    : IDistributedEventHandler<ProjectPurgedEto>, ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task HandleEventAsync(ProjectPurgedEto eventData)
    {
        var projectId = eventData.ProjectId;

        // Deleted tickets and goals are still the space's data.
        using var _ = dataFilter.Disable<ISoftDelete>();
        var ticketIds = (await tickets.GetQueryableAsync()).Where(ticket => ticket.ProjectId == projectId).Select(ticket => ticket.Id);

        // Files first: a retry finds the rows still there and deletes what is left.
        var attachmentKeys = await (await attachments.GetQueryableAsync())
            .Where(attachment => ticketIds.Contains(attachment.TicketId)).Select(attachment => attachment.StorageKey).ToListAsync();
        var richTextIds = await (await richTextFiles.GetQueryableAsync())
            .Where(file => file.ProjectId == projectId).Select(file => file.Id).ToListAsync();
        foreach (var key in attachmentKeys.Concat(richTextIds.Select(id => $"rich-text/{id:N}")))
        {
            await container.DeleteAsync(key);
        }

        foreach (var session in await uploads.GetListAsync(session => session.ProjectId == projectId))
        {
            await uploadStorage.AbortAsync(session);
        }

        var commentIds = (await comments.GetQueryableAsync()).Where(comment => ticketIds.Contains(comment.TicketId)).Select(comment => comment.Id);
        await reactions.DeleteDirectAsync(reaction => commentIds.Contains(reaction.CommentId));
        await comments.DeleteDirectAsync(comment => ticketIds.Contains(comment.TicketId));
        await attachments.DeleteDirectAsync(attachment => ticketIds.Contains(attachment.TicketId));
        await activities.DeleteDirectAsync(activity => ticketIds.Contains(activity.TicketId));
        await workLogs.DeleteDirectAsync(log => ticketIds.Contains(log.TicketId));
        await watchers.DeleteDirectAsync(watcher => ticketIds.Contains(watcher.TicketId));
        await ticketLabels.DeleteDirectAsync(link => ticketIds.Contains(link.TicketId));
        await fieldValues.DeleteDirectAsync(value => ticketIds.Contains(value.TicketId));
        await recents.DeleteDirectAsync(recent => ticketIds.Contains(recent.TicketId));

        // Links to tickets of other spaces go too; those tickets keep their history line.
        await links.DeleteDirectAsync(link => ticketIds.Contains(link.SourceTicketId) || ticketIds.Contains(link.TargetTicketId));

        var goalIds = (await goals.GetQueryableAsync()).Where(goal => goal.ProjectId == projectId).Select(goal => goal.Id);
        await goalUpdates.DeleteDirectAsync(update => goalIds.Contains(update.GoalId));
        await goals.DeleteDirectAsync(goal => goal.ProjectId == projectId);

        await uploads.DeleteDirectAsync(session => session.ProjectId == projectId);
        await richTextFiles.DeleteDirectAsync(file => file.ProjectId == projectId);
        await introductions.DeleteDirectAsync(introduction => introduction.ProjectId == projectId);
        await labels.DeleteDirectAsync(label => label.ProjectId == projectId);
        await templates.DeleteDirectAsync(template => template.ProjectId == projectId);

        // The space's own custom fields go; workspace fields only lose the space's settings.
        var ownFieldIds = (await customFields.GetQueryableAsync()).Where(field => field.ProjectId == projectId).Select(field => field.Id);
        await spaceFields.DeleteDirectAsync(row => row.ProjectId == projectId || ownFieldIds.Contains(row.FieldId));
        await customFieldOptions.DeleteDirectAsync(option => ownFieldIds.Contains(option.FieldId));
        await customFields.DeleteDirectAsync(field => field.ProjectId == projectId);
        await tickets.DeleteDirectAsync(ticket => ticket.ProjectId == projectId);
        await types.DeleteDirectAsync(type => type.ProjectId == projectId);
    }
}
