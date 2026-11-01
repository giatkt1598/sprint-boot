using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SprintBoot.Identity;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;

namespace SprintBoot.Host.Mcp;

/// <summary>Thin adapters: the app services decide, with the token owner's permissions.</summary>
[McpServerToolType]
public class SprintBootMcpTools(
    IProjectAppService projects,
    IIssueAppService issues,
    ICommentAppService comments,
    ITransitionAppService transitions,
    IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "list_spaces", ReadOnly = true), Description("Lists the spaces you can see, with their keys.")]
    public Task<string> ListSpaces() => RunAsync(async () =>
        (await projects.GetListAsync()).Select(space => new { space.Key, space.Name, Type = space.Type.ToString() }));

    [McpServerTool(Name = "search_issues", ReadOnly = true), Description("Finds issues by text, optionally within one space.")]
    public Task<string> SearchIssues(
        [Description("Words to look for in the key or title.")] string? text = null,
        [Description("Space key, such as BANK.")] string? space = null,
        [Description("How many issues to return, at most 50.")] int limit = 20) => RunAsync(async () =>
    {
        var input = new IssueSearchInput { Text = text, MaxResultCount = Math.Clamp(limit, 1, 50) };
        if (space is not null)
        {
            input.ProjectId = (await projects.GetByKeyAsync(space)).Id;
        }

        var result = await issues.GetSearchAsync(input);
        return result.Items.Select(issue => new { issue.Key, issue.Title, Status = issue.Status.Name, Assignee = issue.Assignee?.Name });
    });

    [McpServerTool(Name = "get_issue", ReadOnly = true), Description("Reads one issue with its description, comments and subtasks.")]
    public Task<string> GetIssue([Description("Issue key, such as BANK-12.")] string key) => RunAsync(async () =>
    {
        var issue = await issues.GetByKeyAsync(key);
        var thread = await comments.GetListAsync(issue.Id);
        return new
        {
            issue.Key,
            issue.Title,
            issue.Description,
            Type = issue.Type.Name,
            Status = issue.Status.Name,
            Priority = issue.Priority.Name,
            Assignee = issue.Assignee?.Name,
            Reporter = issue.Reporter?.Name,
            Parent = issue.Parent?.Key,
            Children = issue.Children.Select(child => child.Key),
            issue.Labels,
            issue.StoryPoints,
            issue.DueDate,
            Comments = thread.Select(comment => new { Author = comment.Author?.Name, comment.Body, comment.CreatedAt }),
        };
    });

    [McpServerTool(Name = "list_transitions", ReadOnly = true), Description("Lists the statuses an issue can move to now.")]
    public Task<string> ListTransitions([Description("Issue key.")] string key) => RunAsync(async () =>
    {
        var issue = await issues.GetByKeyAsync(key);
        return (await transitions.GetAvailableAsync(issue.Id))
            .Select(item => new { Status = item.ToStatus.Name, item.RequiresComment, item.MissingFields });
    });

    [McpServerTool(Name = "create_issue"), Description("Creates an issue in a space.")]
    public Task<string> CreateIssue(
        [Description("Space key.")] string space,
        [Description("Issue title.")] string title,
        [Description("Issue type name: Task, Story, Bug or Epic.")] string type = "Task",
        [Description("Plain text description.")] string? description = null) => WriteAsync(async () =>
    {
        var project = await projects.GetByKeyAsync(space);
        var match = (await issues.GetTypesAsync(project.Id)).FirstOrDefault(item => string.Equals(item.Name, type, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"This space has no issue type named {type}.");
        var issue = await issues.CreateAsync(new CreateIssueInput
        {
            ProjectId = project.Id,
            TypeId = match.Id,
            Title = title,
            Description = description is null ? null : ToHtml(description),
        });
        return new { issue.Key, issue.Title, Status = issue.Status.Name };
    });

    [McpServerTool(Name = "update_issue"), Description("Changes the title or description of an issue.")]
    public Task<string> UpdateIssue(
        [Description("Issue key.")] string key,
        [Description("New title; leave out to keep it.")] string? title = null,
        [Description("New plain text description; leave out to keep it.")] string? description = null) => WriteAsync(async () =>
    {
        var issue = await issues.GetByKeyAsync(key);
        var fields = new Dictionary<string, JsonElement>();
        if (title is not null)
        {
            fields["title"] = JsonSerializer.SerializeToElement(title);
        }

        if (description is not null)
        {
            fields["description"] = JsonSerializer.SerializeToElement(ToHtml(description));
        }

        var updated = await issues.UpdateAsync(issue.Id, new UpdateIssueInput { ConcurrencyStamp = issue.ConcurrencyStamp, Fields = fields });
        return new { updated.Key, updated.Title };
    });

    [McpServerTool(Name = "transition_issue"), Description("Moves an issue to another status, following its workflow.")]
    public Task<string> TransitionIssue(
        [Description("Issue key.")] string key,
        [Description("Status name, from list_transitions.")] string status,
        [Description("Comment, when the move requires one.")] string? comment = null) => WriteAsync(async () =>
    {
        var issue = await issues.GetByKeyAsync(key);
        var option = (await transitions.GetAvailableAsync(issue.Id))
            .FirstOrDefault(item => string.Equals(item.ToStatus.Name, status, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"{key} cannot move to {status} now.");
        await transitions.ExecuteAsync(issue.Id, new ExecuteTransitionInput
        {
            TransitionId = option.Id,
            ConcurrencyStamp = issue.ConcurrencyStamp,
            Comment = comment,
        });
        return new { Key = key, Status = option.ToStatus.Name };
    });

    [McpServerTool(Name = "add_comment"), Description("Adds a comment to an issue.")]
    public Task<string> AddComment([Description("Issue key.")] string key, [Description("Plain text comment.")] string body) => WriteAsync(async () =>
    {
        var issue = await issues.GetByKeyAsync(key);
        var comment = await comments.CreateAsync(new CreateCommentInput { IssueId = issue.Id, Body = ToHtml(body) });
        return new { Key = key, comment.Id };
    });

    private static string ToHtml(string text) =>
        string.Concat(text.Split('\n').Select(line => $"<p>{WebUtility.HtmlEncode(line.TrimEnd())}</p>"));

    private Task<string> WriteAsync<T>(Func<Task<T>> action) =>
        http.HttpContext is { } context && PersonalTokenGate.IsReadOnly(context.User)
            ? throw new McpException("This token is read-only.")
            : RunAsync(action);

    /// <summary>The client only sees exceptions raised as MCP errors, so domain refusals become those.</summary>
    private static async Task<string> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return JsonSerializer.Serialize(await action(), Json);
        }
        catch (BusinessException exception)
        {
            throw new McpException(exception.Code ?? "The request was refused.");
        }
        catch (EntityNotFoundException)
        {
            throw new McpException("Not found.");
        }
        catch (AbpAuthorizationException)
        {
            throw new McpException("You do not have permission for this.");
        }
    }
}
