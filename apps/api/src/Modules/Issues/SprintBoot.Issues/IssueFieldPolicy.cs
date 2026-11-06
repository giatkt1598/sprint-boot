using Volo.Abp.DependencyInjection;

namespace SprintBoot.Issues;

/// <summary>Every read and write of an issue field goes through here; field-level permissions plug in later.</summary>
public interface IIssueFieldPolicy
{
    Task<bool> CanWriteAsync(Guid projectId, string fieldKey);

    /// <summary>Last step before an issue leaves the API; may blank fields the caller cannot read.</summary>
    Task ApplyReadAsync(IssueDto issue);
}

public class DefaultIssueFieldPolicy : IIssueFieldPolicy, ITransientDependency
{
    public Task<bool> CanWriteAsync(Guid projectId, string fieldKey) => Task.FromResult(true);

    public Task ApplyReadAsync(IssueDto issue) => Task.CompletedTask;
}

public static class IssueFieldKeys
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
