using SprintBoot.Identity;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SprintBoot.Notifications;

public record NotificationEvent(
    Guid EventId,
    string EventType,
    Guid ProjectId,
    Guid? IssueId,
    Guid ActorId,
    IReadOnlyCollection<Guid> Mentioned,
    string? Title = null,
    string? Detail = null);

/// <summary>Turns one business event into notifications for the people the scheme selects.</summary>
public class NotificationProcessor(
    IRepository<Notification, Guid> notifications,
    IRepository<NotificationRule, Guid> rules,
    IRepository<NotificationPreference, Guid> preferences,
    SchemeResolver schemes,
    IIssueFactsProvider issueFacts,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    NotificationPusher pusher,
    IUnitOfWorkManager unitOfWorkManager,
    IClock clock,
    IGuidGenerator guids) : ITransientDependency
{
    public static readonly TimeSpan MergeWindow = TimeSpan.FromMinutes(5);

    public async Task ProcessAsync(NotificationEvent item)
    {
        var facts = item.IssueId is { } issueId ? await issueFacts.FindAsync(issueId) : null;
        if (item.IssueId is not null && facts is null)
        {
            return;
        }

        var schemeId = (await schemes.GetForProjectAsync(item.ProjectId)).Id;
        var ruleList = await rules.GetListAsync(rule => rule.SchemeId == schemeId && rule.EventType == item.EventType);
        var members = await projectAccess.GetMemberRolesAsync(item.ProjectId);

        var recipients = new HashSet<Guid>(item.Mentioned);
        foreach (var rule in ruleList)
        {
            recipients.UnionWith(Resolve(rule, facts, item, members));
        }

        recipients.Remove(item.ActorId);
        // Membership is the viewing right the event relies on; leaving a project stops its notifications.
        recipients.IntersectWith(members.Keys);
        if (recipients.Count == 0)
        {
            return;
        }

        recipients.IntersectWith(await userDirectory.GetActiveIdsAsync(recipients));
        var muted = await preferences.GetListAsync(pref => pref.EventType == item.EventType && !pref.Enabled && recipients.Contains(pref.UserId));
        recipients.ExceptWith(muted.Select(pref => pref.UserId));

        var already = await notifications.GetListAsync(row => row.EventId == item.EventId && recipients.Contains(row.UserId));
        recipients.ExceptWith(already.Select(row => row.UserId));
        if (recipients.Count == 0)
        {
            return;
        }

        var actor = (await userDirectory.GetByIdsAsync([item.ActorId])).GetValueOrDefault(item.ActorId);
        var now = clock.Now;
        var pushed = new List<Guid>();

        foreach (var userId in recipients)
        {
            pushed.Add(userId);
            if (item.EventType == NotificationEventTypes.IssueUpdated && await TryMergeAsync(item, userId, now))
            {
                continue;
            }

            await notifications.InsertAsync(new Notification(
                guids.Create(), userId, item.EventType, item.EventId, item.IssueId, item.ProjectId, item.ActorId,
                facts?.Key, facts?.Title ?? item.Title, actor?.Name, item.Detail, now), autoSave: true);
        }

        // After commit, or clients would refetch before the rows are visible.
        if (unitOfWorkManager.Current is { } unitOfWork)
        {
            unitOfWork.OnCompleted(() => pusher.PushAsync(pushed));
        }
        else
        {
            await pusher.PushAsync(pushed);
        }
    }

    private async Task<bool> TryMergeAsync(NotificationEvent item, Guid userId, DateTime now)
    {
        var since = now - MergeWindow;
        var recent = await notifications.FirstOrDefaultAsync(row =>
            row.UserId == userId
            && row.IssueId == item.IssueId
            && row.ActorId == item.ActorId
            && row.EventType == item.EventType
            && row.ReadAt == null
            && row.CreatedAt >= since);
        if (recent is null)
        {
            return false;
        }

        var fields = (recent.Detail ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Concat((item.Detail ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries));
        recent.Detail = string.Join(",", fields.Distinct());
        recent.MergedCount++;
        recent.CreatedAt = now;
        await notifications.UpdateAsync(recent);
        return true;
    }

    private static IEnumerable<Guid> Resolve(NotificationRule rule, IssueFacts? facts, NotificationEvent item, Dictionary<Guid, string> members)
    {
        switch (rule.RecipientType)
        {
            case RecipientType.Assignee when facts?.AssigneeId is { } assignee:
                return [assignee];
            case RecipientType.Reporter when facts?.ReporterId is { } reporter:
                return [reporter];
            case RecipientType.Watcher when facts is not null:
                return facts.Watchers;
            case RecipientType.Mentioned:
                return item.Mentioned;
            case RecipientType.ProjectRole:
                return members.Where(member => member.Value == rule.RecipientValue).Select(member => member.Key);
            case RecipientType.User when Guid.TryParse(rule.RecipientValue, out var userId):
                return [userId];
            default:
                return [];
        }
    }
}

public class SchemeResolver(
    IRepository<NotificationScheme, Guid> schemes,
    IRepository<ProjectNotificationScheme, Guid> assignments) : ITransientDependency
{
    public async Task<NotificationScheme> GetForProjectAsync(Guid projectId)
    {
        var assignment = await assignments.FirstOrDefaultAsync(item => item.ProjectId == projectId);
        return assignment is null
            ? await schemes.GetAsync(scheme => scheme.IsDefault)
            : await schemes.GetAsync(assignment.SchemeId);
    }
}
