using Volo.Abp.EventBus;

namespace SprintBoot.Tickets;

[EventName("SprintBoot.Tickets.TicketCreated")]
public class TicketCreatedEto
{
    public Guid EventId { get; set; }

    public Guid TicketId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    public Guid[] Mentioned { get; set; } = [];
}

/// <summary>The ticket's description or one of its comments changed, so the links written in them may have too.</summary>
[EventName("SprintBoot.Tickets.TextChanged")]
public class TicketTextChangedEto
{
    public Guid TicketId { get; set; }

    public Guid ProjectId { get; set; }
}

[EventName("SprintBoot.Tickets.TicketUpdated")]
public class TicketUpdatedEto
{
    public Guid EventId { get; set; }

    public Guid TicketId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    public string[] Fields { get; set; } = [];

    public Guid[] Mentioned { get; set; } = [];
}

[EventName("SprintBoot.Tickets.CommentAdded")]
public class CommentAddedEto
{
    public Guid EventId { get; set; }

    public Guid CommentId { get; set; }

    public Guid TicketId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    public Guid[] Mentioned { get; set; } = [];

    /// <summary>Set when the comment is a reply: the author of the comment it answers.</summary>
    public Guid? ParentAuthorId { get; set; }
}

/// <summary>Someone reacted to a comment. The id is the same for the same person, comment and emoji, so reacting again never notifies twice.</summary>
[EventName("SprintBoot.Tickets.CommentReacted")]
public class CommentReactedEto
{
    public Guid EventId { get; set; }

    public Guid CommentId { get; set; }

    public Guid TicketId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    /// <summary>Whose comment it is.</summary>
    public Guid CommentAuthorId { get; set; }

    public string Emoji { get; set; } = string.Empty;
}
