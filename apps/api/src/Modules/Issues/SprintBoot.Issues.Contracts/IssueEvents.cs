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
}
