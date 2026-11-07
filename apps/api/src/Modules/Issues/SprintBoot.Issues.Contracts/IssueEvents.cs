using Volo.Abp.EventBus;

namespace SprintBoot.Issues;

[EventName("SprintBoot.Issues.IssueCreated")]
public class IssueCreatedEto
{
    public Guid EventId { get; set; }

    public Guid IssueId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    public Guid[] Mentioned { get; set; } = [];
}

[EventName("SprintBoot.Issues.IssueUpdated")]
public class IssueUpdatedEto
{
    public Guid EventId { get; set; }

    public Guid IssueId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    public string[] Fields { get; set; } = [];

    public Guid[] Mentioned { get; set; } = [];
}

[EventName("SprintBoot.Issues.CommentAdded")]
public class CommentAddedEto
{
    public Guid EventId { get; set; }

    public Guid CommentId { get; set; }

    public Guid IssueId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    public Guid[] Mentioned { get; set; } = [];

    /// <summary>Set when the comment is a reply: the author of the comment it answers.</summary>
    public Guid? ParentAuthorId { get; set; }
}

/// <summary>Someone reacted to a comment. The id is the same for the same person, comment and emoji, so reacting again never notifies twice.</summary>
[EventName("SprintBoot.Issues.CommentReacted")]
public class CommentReactedEto
{
    public Guid EventId { get; set; }

    public Guid CommentId { get; set; }

    public Guid IssueId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ActorId { get; set; }

    /// <summary>Whose comment it is.</summary>
    public Guid CommentAuthorId { get; set; }

    public string Emoji { get; set; } = string.Empty;
}
