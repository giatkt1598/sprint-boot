using Volo.Abp.DependencyInjection;

namespace SprintBoot.Tickets;

/// <summary>Every read and write of a ticket field goes through here; field-level permissions plug in later.</summary>
public interface ITicketFieldPolicy
{
    Task<bool> CanWriteAsync(Guid projectId, string fieldKey);

    /// <summary>Last step before a ticket leaves the API; may blank fields the caller cannot read.</summary>
    Task ApplyReadAsync(TicketDto ticket);
}

public class DefaultTicketFieldPolicy : ITicketFieldPolicy, ITransientDependency
{
    public Task<bool> CanWriteAsync(Guid projectId, string fieldKey) => Task.FromResult(true);

    public Task ApplyReadAsync(TicketDto ticket) => Task.CompletedTask;
}

public static class TicketFieldKeys
{
    public const string Title = "title";
    public const string Description = "description";
    public const string Type = "type";
    public const string Priority = "priority";
    public const string Assignee = "assignee";
    public const string Reporter = "reporter";
    public const string Parent = "parent";
    public const string Labels = "labels";
    public const string StoryPoints = "storyPoints";
    public const string StartDate = "startDate";
    public const string DueDate = "dueDate";
    public const string Status = "status";
    public const string WorkLog = "workLog";
    public const string Attachment = "attachment";
    public const string Link = "link";
}
