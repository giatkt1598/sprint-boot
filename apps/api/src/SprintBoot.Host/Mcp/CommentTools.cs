using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SprintBoot.Tickets;

namespace SprintBoot.Host.Mcp;

[McpServerToolType]
public class CommentTools(McpRunner runner, McpResolver resolver, ICommentAppService comments)
{
    [McpServerTool(Name = "list_comments", ReadOnly = true), Description("Reads the comment thread of a ticket in Markdown, replies under their parent.")]
    public Task<string> ListComments([Description("Ticket key.")] string key) => runner.ReadAsync(async () =>
    {
        var ticket = await resolver.TicketAsync(key);
        return new { ticket.Key, Comments = McpViews.Thread(await comments.GetListAsync(ticket.Id)) };
    });

    [McpServerTool(Name = "add_comment", Destructive = false), Description(
        "Adds a comment in Markdown, or a reply to a comment. Mention people with @[Name](user:email).")]
    public Task<string> AddComment(
        [Description("Ticket key.")] string key,
        [Description("Comment in Markdown.")] string body,
        [Description("Id of the comment to reply to, from list_comments.")] string? replyTo = null) => runner.WriteAsync(async () =>
    {
        var ticket = await resolver.TicketAsync(key);
        var comment = await comments.CreateAsync(new CreateCommentInput
        {
            TicketId = ticket.Id,
            ParentId = replyTo is null ? null : Id(replyTo),
            Body = await RichTextMarkdown.ToHtmlAsync(body, resolver.MentionAsync),
        });
        return new { ticket.Key, comment.Id };
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
