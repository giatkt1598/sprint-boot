using System.ComponentModel;
using ModelContextProtocol.Server;
using SprintBoot.Agile;
using SprintBoot.Identity;
using SprintBoot.Issues;
using SprintBoot.Projects;

namespace SprintBoot.Host.Mcp;

/// <summary>What an agent needs before writing: spaces, the values their fields accept, and people.</summary>
[McpServerToolType]
public class LookupTools(
    McpRunner runner,
    McpResolver resolver,
    IProjectAppService projects,
    IIssueAppService issues,
    ISprintAppService sprints,
    IUserDirectoryAppService users)
{
    [McpServerTool(Name = "list_spaces", ReadOnly = true), Description("Lists the spaces you can see, with their keys.")]
    public Task<string> ListSpaces() => runner.ReadAsync(async () =>
        (await projects.GetListAsync()).Select(space => new
        {
            space.Key,
            space.Name,
            Board = space.Type.ToString(),
            Lead = McpViews.Person(space.Lead),
            Issues = space.IssueCount,
        }));

    [McpServerTool(Name = "get_space_fields", ReadOnly = true), Description(
        "Lists the values the issue fields of a space accept: issue types, priorities, statuses, labels, epics and open sprints.")]
    public Task<string> GetSpaceFields([Description("Space key.")] string space) => runner.ReadAsync(async () =>
    {
        var project = await resolver.SpaceAsync(space);
        var openSprints = project.Type == BoardType.Scrum
            ? (await sprints.GetBacklogAsync(project.Id)).Sprints.Select(item => $"{item.Sprint.Name} ({item.Sprint.State})").ToList()
            : null;
        return new
        {
            project.Key,
            project.Name,
            Board = project.Type.ToString(),
            Types = (await resolver.TypesAsync(project.Id)).Select(type => new { type.Name, Level = type.Hierarchy.ToString() }),
            Priorities = (await issues.GetPrioritiesAsync()).OrderBy(priority => priority.Order).Select(priority => priority.Name),
            Statuses = (await resolver.StatusesAsync(project.Id)).Select(status => new { status.Name, Category = status.Category.ToString() }),
            Labels = await issues.GetLabelsAsync(project.Id),
            Epics = (await issues.GetEpicsAsync(project.Id)).Select(epic => new { epic.Key, epic.Title, Status = epic.Status.Name }),
            Sprints = openSprints,
        };
    });

    [McpServerTool(Name = "find_users", ReadOnly = true), Description("Finds people by name or email, to assign or mention them.")]
    public Task<string> FindUsers([Description("Part of a name or email.")] string query) => runner.ReadAsync(async () =>
        (await users.GetSearchAsync(query)).Select(user => new { user.Name, user.Email, user.Id }));
}
