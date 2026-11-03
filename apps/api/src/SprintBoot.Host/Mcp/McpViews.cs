using SprintBoot.Agile;
using SprintBoot.Identity;
using SprintBoot.Issues;

namespace SprintBoot.Host.Mcp;

/// <summary>Compact shapes for tool results: names instead of ids, so agents can pass them straight back.</summary>
public static class McpViews
{
    public static string? Person(UserRefDto? user) =>
        user is null ? null : string.IsNullOrEmpty(user.Email) ? user.Name : $"{user.Name} <{user.Email}>";

    public static object Card(IssueCardDto card) => new
    {
        card.Key,
        card.Title,
        Type = card.Type.Name,
        Status = card.Status.Name,
        Priority = card.Priority.Name,
        Assignee = Person(card.Assignee),
        Labels = card.Labels.Length == 0 ? null : card.Labels,
        Epic = card.EpicTitle,
        card.StoryPoints,
    };

    public static object Ref(IssueRefDto issue) => new
    {
        issue.Key,
        issue.Title,
        Status = issue.Status.Name,
        Assignee = Person(issue.Assignee),
    };

    public static object Sprint(SprintSummaryDto sprint) => new
    {
        sprint.Name,
        State = sprint.State.ToString(),
        sprint.Goal,
        sprint.StartDate,
        sprint.EndDate,
        sprint.IssueCount,
        sprint.TotalPoints,
        sprint.DonePoints,
    };

    public static object Comment(CommentDto comment, IEnumerable<object>? replies = null) => new
    {
        comment.Id,
        Author = Person(comment.Author),
        Body = RichTextMarkdown.ToMarkdown(comment.Body),
        comment.CreatedAt,
        Edited = comment.IsEdited ? true : (bool?)null,
        Reactions = comment.Reactions.Count == 0 ? null : comment.Reactions.Select(reaction => $"{reaction.Emoji} {reaction.Count}"),
        Replies = replies,
    };

    /// <summary>Replies sit under their parent, as the issue page shows them.</summary>
    public static List<object> Thread(List<CommentDto> comments)
    {
        var byParent = comments.Where(comment => comment.ParentId is not null).ToLookup(comment => comment.ParentId!.Value);
        return [.. comments
            .Where(comment => comment.ParentId is null)
            .Select(comment => Comment(comment, byParent.Contains(comment.Id) ? byParent[comment.Id].Select(reply => Comment(reply)).ToList() : null))];
    }

    public static string Duration(int minutes) =>
        minutes < 60 ? $"{minutes}m" : minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m";
}
