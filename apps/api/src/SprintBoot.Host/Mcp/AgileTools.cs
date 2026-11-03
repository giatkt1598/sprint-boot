using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SprintBoot.Agile;
using SprintBoot.Projects;

namespace SprintBoot.Host.Mcp;

[McpServerToolType]
public class AgileTools(McpRunner runner, McpResolver resolver, ISprintAppService sprints, IBoardAppService boards)
{
    private const int MaxBacklogRows = 100;

    [McpServerTool(Name = "get_backlog", ReadOnly = true), Description(
        "Reads the planning view of a space: open sprints with their issues in rank order, then the backlog.")]
    public Task<string> GetBacklog(
        [Description("Space key.")] string space,
        [Description("Most backlog issues to return, at most 100.")] int limit = 50) => runner.ReadAsync(async () =>
    {
        var project = await resolver.SpaceAsync(space);
        var view = await sprints.GetBacklogAsync(project.Id);
        var take = Math.Clamp(limit, 1, MaxBacklogRows);
        return new
        {
            Space = project.Key,
            Board = view.Type.ToString(),
            Sprints = view.Sprints.Select(item => new { Sprint = McpViews.Sprint(item.Sprint), Issues = item.Issues.Select(McpViews.Card) }),
            BacklogTotal = view.Backlog.Count,
            Backlog = view.Backlog.Take(take).Select(McpViews.Card),
            ClosedSprints = view.ClosedSprints.Count == 0 ? null : view.ClosedSprints.Select(sprint => sprint.Name),
        };
    });

    [McpServerTool(Name = "get_board", ReadOnly = true), Description("Reads the board of a space: each column with its issues.")]
    public Task<string> GetBoard([Description("Space key.")] string space) => runner.ReadAsync(async () =>
    {
        var project = await resolver.SpaceAsync(space);
        var board = await boards.GetViewAsync(project.Id);
        return new
        {
            Space = project.Key,
            Board = board.Type.ToString(),
            ActiveSprint = board.ActiveSprint is null ? null : McpViews.Sprint(board.ActiveSprint),
            Columns = board.Columns.Where(column => !column.IsHidden).Select(column =>
            {
                var statusIds = column.Statuses.Select(status => status.Id).ToHashSet();
                return new
                {
                    column.Name,
                    Statuses = column.Statuses.Select(status => status.Name),
                    Limit = column.MaxLimit,
                    Issues = board.Cards.Where(card => statusIds.Contains(card.Status.Id)).Select(McpViews.Card),
                };
            }),
        };
    });

    [McpServerTool(Name = "move_to_sprint", Destructive = false, Idempotent = true), Description(
        "Puts issues into a sprint or back in the backlog, optionally ranked after or before another issue. Epics and subtasks are not planned.")]
    public Task<string> MoveToSprint(
        [Description("Issue keys, all in one space.")] string[] keys,
        [Description("Sprint name, active for the running sprint, or backlog.")] string sprint,
        [Description("Rank the issues right after this issue key.")] string? after = null,
        [Description("Rank the issues right before this issue key.")] string? before = null) => runner.WriteAsync(async () =>
    {
        if (keys.Length == 0)
        {
            throw new McpException("Give at least one issue key.");
        }

        var first = await resolver.IssueAsync(keys[0]);
        var target = await resolver.SprintAsync(first.ProjectId, sprint);
        Guid? afterId = after is null ? null : (await resolver.IssueAsync(after)).Id;
        Guid? beforeId = before is null ? null : (await resolver.IssueAsync(before)).Id;
        var moved = new List<string>();
        foreach (var key in keys)
        {
            var issue = await resolver.IssueAsync(key);
            if (issue.ProjectId != first.ProjectId)
            {
                throw new McpException($"{issue.Key} is in another space than {first.Key}.");
            }

            // Each moved issue goes after the previous one, so the list keeps the order given.
            await sprints.MoveIssueAsync(issue.Id, new MoveIssueInput { SprintId = target.Id, AfterIssueId = afterId, BeforeIssueId = afterId is null ? beforeId : null });
            if (afterId is not null || beforeId is not null)
            {
                afterId = issue.Id;
            }

            moved.Add(issue.Key);
        }

        return new { Sprint = target.Name, Moved = moved };
    });
}
