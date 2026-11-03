using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SprintBoot.Identity;
using SprintBoot.Issues;
using Volo.Abp.Timing;

namespace SprintBoot.Host.Mcp;

/// <summary>Time tracking, watching and history of an issue.</summary>
[McpServerToolType]
public class WorkTools(
    McpRunner runner,
    McpResolver resolver,
    IIssueAppService issues,
    IWorkLogAppService workLogs,
    IWatcherAppService watchers,
    IUserDirectory directory,
    IClock clock)
{
    private static readonly HashSet<string> PersonFields = [IssueFieldKeys.Assignee, IssueFieldKeys.Reporter];

    [McpServerTool(Name = "list_work_logs", ReadOnly = true), Description("Lists the time logged on an issue.")]
    public Task<string> ListWorkLogs([Description("Issue key.")] string key) => runner.ReadAsync(async () =>
    {
        var issue = await resolver.IssueAsync(key);
        var list = await workLogs.GetListAsync(issue.Id);
        return new
        {
            issue.Key,
            Total = McpViews.Duration(list.TotalMinutes),
            Logs = list.Items.Select(log => new { log.Id, Author = McpViews.Person(log.Author), Time = McpViews.Duration(log.Minutes), log.WorkDate, log.Note }),
        };
    });

    [McpServerTool(Name = "log_work", Destructive = false), Description("Logs time spent on an issue.")]
    public Task<string> LogWork(
        [Description("Issue key.")] string key,
        [Description("Time spent: minutes, or forms like 45m, 1h 30m or 1.5h.")] string time,
        [Description("Day of the work, like 2026-11-30. Defaults to today.")] string? date = null,
        [Description("What was done.")] string? note = null) => runner.WriteAsync(async () =>
    {
        var issue = await resolver.IssueAsync(key);
        var log = await workLogs.CreateAsync(new CreateWorkLogInput
        {
            IssueId = issue.Id,
            Minutes = McpResolver.Minutes(time),
            WorkDate = date is null ? DateOnly.FromDateTime(clock.Now) : McpResolver.Date(date, "date"),
            Note = note,
        });
        return new { issue.Key, log.Id, Time = McpViews.Duration(log.Minutes), log.WorkDate };
    });

    [McpServerTool(Name = "update_work_log", Destructive = false, Idempotent = true), Description("Changes one of your work logs; leave out what should stay.")]
    public Task<string> UpdateWorkLog(
        [Description("Issue key.")] string key,
        [Description("Work log id, from list_work_logs.")] string id,
        [Description("Time spent: minutes, or forms like 45m, 1h 30m or 1.5h.")] string? time = null,
        [Description("Day of the work, like 2026-11-30.")] string? date = null,
        [Description("What was done.")] string? note = null) => runner.WriteAsync(async () =>
    {
        var issue = await resolver.IssueAsync(key);
        var logId = Guid.TryParse(id.Trim(), out var parsed) ? parsed : throw new McpException($"{id} is not a work log id.");
        var current = (await workLogs.GetListAsync(issue.Id)).Items.FirstOrDefault(log => log.Id == logId)
            ?? throw new McpException($"{issue.Key} has no work log {id}.");
        var log = await workLogs.UpdateAsync(logId, new UpdateWorkLogInput
        {
            Minutes = time is null ? current.Minutes : McpResolver.Minutes(time),
            WorkDate = date is null ? current.WorkDate : McpResolver.Date(date, "date"),
            Note = note ?? current.Note,
        });
        return new { issue.Key, log.Id, Time = McpViews.Duration(log.Minutes), log.WorkDate, log.Note };
    });

    [McpServerTool(Name = "list_watchers", ReadOnly = true), Description("Lists who watches an issue and whether you do.")]
    public Task<string> ListWatchers([Description("Issue key.")] string key) => runner.ReadAsync(async () =>
    {
        var issue = await resolver.IssueAsync(key);
        return Watchers(issue.Key, await watchers.GetAsync(issue.Id));
    });

    [McpServerTool(Name = "watch_issue", Destructive = false, Idempotent = true), Description("Starts watching an issue, to be notified of its changes.")]
    public Task<string> WatchIssue([Description("Issue key.")] string key) => runner.WriteAsync(async () =>
    {
        var issue = await resolver.IssueAsync(key);
        return Watchers(issue.Key, await watchers.WatchAsync(issue.Id));
    });

    [McpServerTool(Name = "unwatch_issue", Destructive = false, Idempotent = true), Description("Stops watching an issue.")]
    public Task<string> UnwatchIssue([Description("Issue key.")] string key) => runner.WriteAsync(async () =>
    {
        var issue = await resolver.IssueAsync(key);
        return Watchers(issue.Key, await watchers.UnwatchAsync(issue.Id));
    });

    [McpServerTool(Name = "get_issue_activity", ReadOnly = true), Description("Reads the change history of an issue, newest first: who changed which field, from what to what.")]
    public Task<string> GetIssueActivity(
        [Description("Issue key.")] string key,
        [Description("How many changes to return, at most 100.")] int limit = 30) => runner.ReadAsync(async () =>
    {
        var issue = await resolver.IssueAsync(key);
        var rows = (await issues.GetActivityAsync(issue.Id)).OrderByDescending(row => row.CreatedAt).Take(Math.Clamp(limit, 1, 100)).ToList();
        var people = await directory.GetByIdsAsync(rows
            .Where(row => PersonFields.Contains(row.Field))
            .SelectMany(row => new[] { row.OldValue, row.NewValue })
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty));

        string? Value(string field, string? value)
        {
            if (value is null)
            {
                return null;
            }

            if (PersonFields.Contains(field) && Guid.TryParse(value, out var id))
            {
                return McpViews.Person(people.GetValueOrDefault(id)) ?? value;
            }

            if (field == IssueFieldKeys.Description)
            {
                var text = RichTextMarkdown.ToMarkdown(value);
                return text.Length <= 300 ? text : text[..300] + "…";
            }

            return value;
        }

        return new
        {
            issue.Key,
            Activity = rows.Select(row => new
            {
                At = row.CreatedAt,
                By = McpViews.Person(row.Actor),
                Via = row.ActorType == ActorType.User ? null : row.ActorType.ToString(),
                row.Field,
                From = Value(row.Field, row.OldValue),
                To = Value(row.Field, row.NewValue),
            }),
        };
    });

    private static object Watchers(string key, WatchersDto watchers) => new
    {
        Key = key,
        watchers.Count,
        watchers.WatchedByMe,
        Watchers = watchers.Users.Select(McpViews.Person),
    };
}
