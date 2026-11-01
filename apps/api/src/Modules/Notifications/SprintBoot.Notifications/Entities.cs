using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.Notifications;

public class NotificationScheme : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsDefault { get; private set; }

    protected NotificationScheme()
    {
    }

    public NotificationScheme(Guid id, string name, string? description, bool isDefault)
        : base(id)
    {
        Name = name;
        Description = description;
        IsDefault = isDefault;
    }
}

public class NotificationRule : Entity<Guid>
{
    public Guid SchemeId { get; private set; }

    public string EventType { get; private set; } = null!;

    public RecipientType RecipientType { get; private set; }

    public string? RecipientValue { get; private set; }

    protected NotificationRule()
    {
    }

    public NotificationRule(Guid id, Guid schemeId, string eventType, RecipientType recipientType, string? recipientValue)
        : base(id)
    {
        SchemeId = schemeId;
        EventType = eventType;
        RecipientType = recipientType;
        RecipientValue = recipientValue;
    }
}

public class ProjectNotificationScheme : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public Guid SchemeId { get; set; }

    protected ProjectNotificationScheme()
    {
    }

    public ProjectNotificationScheme(Guid id, Guid projectId, Guid schemeId)
        : base(id)
    {
        ProjectId = projectId;
        SchemeId = schemeId;
    }
}

public class Notification : Entity<Guid>
{
    public Guid UserId { get; private set; }

    public string EventType { get; private set; } = null!;

    /// <summary>With UserId this is unique, so a redelivered event cannot notify twice.</summary>
    public Guid EventId { get; private set; }

    public Guid? IssueId { get; private set; }

    public Guid? ProjectId { get; private set; }

    public Guid ActorId { get; private set; }

    public string? IssueKey { get; private set; }

    public string? Title { get; private set; }

    public string? ActorName { get; private set; }

    public string? Detail { get; set; }

    public int MergedCount { get; set; } = 1;

    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; }

    protected Notification()
    {
    }

    public Notification(Guid id, Guid userId, string eventType, Guid eventId, Guid? issueId, Guid? projectId, Guid actorId,
        string? issueKey, string? title, string? actorName, string? detail, DateTime createdAt)
        : base(id)
    {
        UserId = userId;
        EventType = eventType;
        EventId = eventId;
        IssueId = issueId;
        ProjectId = projectId;
        ActorId = actorId;
        IssueKey = issueKey;
        Title = title;
        ActorName = actorName;
        Detail = detail;
        CreatedAt = createdAt;
    }
}

public class NotificationPreference : Entity<Guid>
{
    public Guid UserId { get; private set; }

    public string EventType { get; private set; } = null!;

    public bool Enabled { get; set; }

    protected NotificationPreference()
    {
    }

    public NotificationPreference(Guid id, Guid userId, string eventType, bool enabled)
        : base(id)
    {
        UserId = userId;
        EventType = eventType;
        Enabled = enabled;
    }
}
