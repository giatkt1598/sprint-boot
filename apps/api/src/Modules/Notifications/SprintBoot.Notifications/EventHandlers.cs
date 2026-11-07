using SprintBoot.Agile;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.Notifications;

public class NotificationEventHandlers(NotificationProcessor processor) :
    IDistributedEventHandler<IssueCreatedEto>,
    IDistributedEventHandler<IssueUpdatedEto>,
    IDistributedEventHandler<CommentAddedEto>,
    IDistributedEventHandler<CommentReactedEto>,
    IDistributedEventHandler<IssueTransitionedEto>,
    IDistributedEventHandler<WorkflowNotificationEto>,
    IDistributedEventHandler<SprintStateChangedEto>,
    IDistributedEventHandler<ProjectMemberAddedEto>,
    ITransientDependency
{
    /// <summary>Mentions take the first slot, so a person mentioned in a comment gets one notification for it.</summary>
    private async Task WithMentionsAsync(NotificationEvent item)
    {
        if (item.Mentioned.Count > 0)
        {
            await processor.ProcessAsync(item with { EventType = NotificationEventTypes.Mentioned });
        }

        await processor.ProcessAsync(item with { Mentioned = [] });
    }

    [UnitOfWork]
    public virtual Task HandleEventAsync(IssueCreatedEto eventData) => WithMentionsAsync(new NotificationEvent(
        eventData.EventId, NotificationEventTypes.IssueCreated, eventData.ProjectId, eventData.IssueId, eventData.ActorId, eventData.Mentioned));

    [UnitOfWork]
    public virtual async Task HandleEventAsync(IssueUpdatedEto eventData)
    {
        if (eventData.Fields.Contains("assignee"))
        {
            await processor.ProcessAsync(new NotificationEvent(
                eventData.EventId, NotificationEventTypes.IssueAssigned, eventData.ProjectId, eventData.IssueId, eventData.ActorId, []));
        }

        await WithMentionsAsync(new NotificationEvent(
            eventData.EventId, NotificationEventTypes.IssueUpdated, eventData.ProjectId, eventData.IssueId, eventData.ActorId,
            eventData.Mentioned, Detail: string.Join(",", eventData.Fields)));
    }

    /// <summary>
    /// The author of the comment a reply answers hears of it first, so they get one notification even when they also
    /// watch the issue or are mentioned: later ones for the same event are skipped.
    /// </summary>
    [UnitOfWork]
    public virtual async Task HandleEventAsync(CommentAddedEto eventData)
    {
        if (eventData.ParentAuthorId is { } parentAuthor)
        {
            await processor.ProcessAsync(new NotificationEvent(
                eventData.EventId, NotificationEventTypes.CommentReply, eventData.ProjectId, eventData.IssueId, eventData.ActorId, [parentAuthor]));
        }

        await WithMentionsAsync(new NotificationEvent(
            eventData.EventId, NotificationEventTypes.CommentAdded, eventData.ProjectId, eventData.IssueId, eventData.ActorId, eventData.Mentioned));
    }

    [UnitOfWork]
    public virtual Task HandleEventAsync(CommentReactedEto eventData) => processor.ProcessAsync(new NotificationEvent(
        eventData.EventId, NotificationEventTypes.CommentReaction, eventData.ProjectId, eventData.IssueId, eventData.ActorId,
        [eventData.CommentAuthorId], Detail: eventData.Emoji));

    [UnitOfWork]
    public virtual Task HandleEventAsync(IssueTransitionedEto eventData) => processor.ProcessAsync(new NotificationEvent(
        eventData.EventId, NotificationEventTypes.IssueTransitioned, eventData.ProjectId, eventData.IssueId, eventData.ActorId, [], Detail: eventData.ToStatusName));

    [UnitOfWork]
    public virtual Task HandleEventAsync(WorkflowNotificationEto eventData) => processor.ProcessAsync(new NotificationEvent(
        eventData.EventId, NotificationEventTypes.WorkflowNotification, eventData.ProjectId, eventData.IssueId, eventData.ActorId, [], Detail: eventData.Message));

    [UnitOfWork]
    public virtual Task HandleEventAsync(SprintStateChangedEto eventData) => processor.ProcessAsync(new NotificationEvent(
        eventData.EventId,
        eventData.State == SprintState.Active ? NotificationEventTypes.SprintStarted : NotificationEventTypes.SprintCompleted,
        eventData.ProjectId, null, eventData.ActorId, [], Title: eventData.SprintName));

    [UnitOfWork]
    public virtual Task HandleEventAsync(ProjectMemberAddedEto eventData) => processor.ProcessAsync(new NotificationEvent(
        eventData.EventId, NotificationEventTypes.MemberAdded, eventData.ProjectId, null, eventData.ActorId, [eventData.UserId]));
}
