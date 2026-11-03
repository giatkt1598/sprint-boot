using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SprintBoot.Issues;

namespace SprintBoot.Host.Mcp;

[McpServerToolType]
public class CommentTools(McpRunner runner, McpResolver resolver, ICommentAppService comments)
{
    [McpServerTool(Name = "list_comments", ReadOnly = true), Description("Reads the comment thread of an issue in Markdown, replies under their parent.")]
    public Task<string> ListComments([Description("Issue key.")] string key) => runner.ReadAsync(async () =>
    {
        var issue = await resolver.IssueAsync(key);
        return new { issue.Key, Comments = McpViews.Thread(await comments.GetListAsync(issue.Id)) };
    });

    [McpServerTool(Name = "add_comment", Destructive = false), Description(
        "Adds a comment in Markdown, or a reply to a comment. Mention people with @[Name](user:email).")]
    public Task<string> AddComment(
        [Description("Issue key.")] string key,
        [Description("Comment in Markdown.")] string body,
        [Description("Id of the comment to reply to, from list_comments.")] string? replyTo = null) => runner.WriteAsync(async () =>
    {
        var issue = await resolver.IssueAsync(key);
        var comment = await comments.CreateAsync(new CreateCommentInput
        {
            IssueId = issue.Id,
            ParentId = replyTo is null ? null : Id(replyTo),
            Body = await RichTextMarkdown.ToHtmlAsync(body, resolver.MentionAsync),
        });
        return new { issue.Key, comment.Id };
    });

    [McpServerTool(Name = "update_comment", Destructive = false, Idempotent = true), Description("Replaces the text of one of your comments.")]
    public Task<string> UpdateComment(
        [Description("Comment id, from list_comments.")] string id,
        [Description("The new comment in Markdown.")] string body) => runner.WriteAsync(async () =>
    {
        var comment = await comments.UpdateAsync(Id(id), new UpdateCommentInput
        {
            Body = await RichTextMarkdown.ToHtmlAsync(body, resolver.MentionAsync),
        });
        return McpViews.Comment(comment);
    });

    private static Guid Id(string text) =>
        Guid.TryParse(text.Trim(), out var id) ? id : throw new McpException($"{text} is not a comment id. Use the id from list_comments.");
}
