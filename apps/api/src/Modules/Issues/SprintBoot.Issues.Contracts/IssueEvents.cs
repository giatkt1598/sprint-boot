using Volo.Abp.EventBus;

namespace SprintBoot.Issues;

[EventName("SprintBoot.Issues.IssueCreated")]
public class IssueCreatedEto
{
    public Guid IssueId { get; set; }

    public Guid ProjectId { get; set; }
}

[EventName("SprintBoot.Issues.IssueUpdated")]
public class IssueUpdatedEto
{
    public Guid IssueId { get; set; }

    public Guid ProjectId { get; set; }

    public string[] Fields { get; set; } = [];
}

[EventName("SprintBoot.Issues.CommentAdded")]
public class CommentAddedEto
{
    public Guid CommentId { get; set; }

    public Guid IssueId { get; set; }

    public Guid ProjectId { get; set; }
}
