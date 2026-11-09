using NpgsqlTypes;
using SprintBoot.Identity;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.Tickets;

public class TicketType : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Icon { get; private set; } = null!;

    public TicketHierarchy Hierarchy { get; private set; }

    protected TicketType()
    {
    }

    public TicketType(Guid id, Guid projectId, string name, string icon, TicketHierarchy hierarchy)
        : base(id)
    {
        ProjectId = projectId;
        Name = name;
        Icon = icon;
        Hierarchy = hierarchy;
    }
}

public class Priority : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public int Order { get; private set; }

    protected Priority()
    {
    }

    public Priority(Guid id, string name, int order)
        : base(id)
    {
        Name = name;
        Order = order;
    }
}

public class Ticket : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public int Number { get; private set; }

    public Guid TypeId { get; set; }

    public string Title { get; set; } = null!;

    public string Description { get; private set; } = string.Empty;

    public string DescriptionText { get; private set; } = string.Empty;

    public Guid StatusId { get; private set; }

    public Guid? WorkflowVersionId { get; private set; }

    public Guid PriorityId { get; set; }

    public string? Resolution { get; private set; }

    public Guid? AssigneeId { get; set; }

    public ActorType? AssigneeType { get; set; }

    public Guid? ReporterId { get; set; }

    public Guid? ParentId { get; set; }

    public decimal? StoryPoints { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public string Rank { get; private set; } = string.Empty;

    public NpgsqlTsVector SearchVector { get; private set; } = null!;

    protected Ticket()
    {
    }

    public Ticket(Guid id, Guid projectId, int number, Guid typeId, string title, Guid statusId, Guid workflowVersionId, Guid priorityId)
        : base(id)
    {
        WorkflowVersionId = workflowVersionId;
        ProjectId = projectId;
        Number = number;
        TypeId = typeId;
        Title = title;
        StatusId = statusId;
        PriorityId = priorityId;
    }

    /// <summary>Only the workflow gateway calls this; nothing else writes status.</summary>
    public void MoveTo(Guid statusId) => StatusId = statusId;

    public void SetRank(string rank) => Rank = rank;

    public void SetWorkflowVersion(Guid versionId) => WorkflowVersionId = versionId;

    public void SetResolution(string? resolution) => Resolution = resolution;

    public void SetDescription(string html, string text)
    {
        Description = html;
        DescriptionText = text;
    }
}

public class TicketWatcher : Entity<Guid>
{
    public Guid TicketId { get; private set; }

    public Guid UserId { get; private set; }

    protected TicketWatcher()
    {
    }

    public TicketWatcher(Guid id, Guid ticketId, Guid userId)
        : base(id)
    {
        TicketId = ticketId;
        UserId = userId;
    }
}

public class Label : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = null!;

    protected Label()
    {
    }

    public Label(Guid id, Guid projectId, string name)
        : base(id)
    {
        ProjectId = projectId;
        Name = name;
    }
}

public class TicketLabel : Entity<Guid>
{
    public Guid TicketId { get; private set; }

    public Guid LabelId { get; private set; }

    protected TicketLabel()
    {
    }

    public TicketLabel(Guid id, Guid ticketId, Guid labelId)
        : base(id)
    {
        TicketId = ticketId;
        LabelId = labelId;
    }
}

public class Comment : CreationAuditedEntity<Guid>
{
    public Guid TicketId { get; private set; }

    public Guid? ParentId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; } = null!;

    public string BodyText { get; private set; } = null!;

    public DateTime? EditedAt { get; private set; }

    protected Comment()
    {
    }

    public Comment(Guid id, Guid ticketId, Guid? parentId, Guid authorId, string body, string bodyText)
        : base(id)
    {
        TicketId = ticketId;
        ParentId = parentId;
        AuthorId = authorId;
        Body = body;
        BodyText = bodyText;
    }

    public void Edit(string body, string bodyText, DateTime at)
    {
        Body = body;
        BodyText = bodyText;
        EditedAt = at;
    }
}

public class CommentReaction : Entity<Guid>
{
    public Guid CommentId { get; private set; }

    public Guid UserId { get; private set; }

    public string Emoji { get; private set; } = null!;

    /// <summary>Orders who reacted, earliest first.</summary>
    public DateTime CreatedAt { get; private set; }

    protected CommentReaction()
    {
    }

    public CommentReaction(Guid id, Guid commentId, Guid userId, string emoji, DateTime createdAt)
        : base(id)
    {
        CreatedAt = createdAt;
        CommentId = commentId;
        UserId = userId;
        Emoji = emoji;
    }
}

public class Attachment : CreationAuditedEntity<Guid>
{
    public Guid TicketId { get; private set; }

    public string FileName { get; private set; } = null!;

    public long Size { get; private set; }

    public string ContentType { get; private set; } = null!;

    public string StorageKey { get; private set; } = null!;

    protected Attachment()
    {
    }

    public Attachment(Guid id, Guid ticketId, string fileName, long size, string contentType, string storageKey)
        : base(id)
    {
        TicketId = ticketId;
        FileName = fileName;
        Size = size;
        ContentType = contentType;
        StorageKey = storageKey;
    }
}

/// <summary>Append-only: no API updates or deletes these rows.</summary>
public class TicketActivity : Entity<Guid>
{
    public Guid TicketId { get; private set; }

    public Guid ActorId { get; private set; }

    public ActorType ActorType { get; private set; }

    public string Field { get; private set; } = null!;

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public DateTime CreatedAt { get; private set; }

    protected TicketActivity()
    {
    }

    public TicketActivity(Guid id, Guid ticketId, ActorRef actor, string field, string? oldValue, string? newValue, DateTime createdAt)
        : base(id)
    {
        TicketId = ticketId;
        ActorId = actor.Id;
        ActorType = actor.Type;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        CreatedAt = createdAt;
    }
}

public class WorkLog : Entity<Guid>
{
    public Guid TicketId { get; private set; }

    public Guid AuthorId { get; private set; }

    public int Minutes { get; set; }

    public DateOnly WorkDate { get; set; }

    public string? Note { get; set; }

    protected WorkLog()
    {
    }

    public WorkLog(Guid id, Guid ticketId, Guid authorId, int minutes, DateOnly workDate, string? note)
        : base(id)
    {
        TicketId = ticketId;
        AuthorId = authorId;
        Minutes = minutes;
        WorkDate = workDate;
        Note = note;
    }
}
