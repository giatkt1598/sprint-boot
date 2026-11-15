using System.ComponentModel;
using ModelContextProtocol.Server;
using SprintBoot.Agile;
using SprintBoot.Identity;
using SprintBoot.Tickets;
using SprintBoot.Projects;

namespace SprintBoot.Host.Mcp;

/// <summary>What an agent needs before writing: spaces, the values their fields accept, and people.</summary>
[McpServerToolType]
public class LookupTools(
    McpRunner runner,
    McpResolver resolver,
    IProjectAppService projects,
    ITicketAppService tickets,
    ISprintAppService sprints,
    IUserDirectoryAppService users,
    McpCustomFields customFields)
{
    [McpServerTool(Name = "list_spaces", ReadOnly = true), Description(
        "Lists the spaces you can see, with their keys. Archived spaces are read-only and left out unless includeArchived is true.")]
    public Task<string> ListSpaces(
        [Description("Also list archived spaces, marked archived: true.")] bool includeArchived = false) => runner.ReadAsync(async () =>
        (await projects.GetListAsync(includeArchived)).Select(space => new
        {
            space.Key,
            space.Name,
            Board = space.Type.ToString(),
            Lead = McpViews.Person(space.Lead),
            Tickets = space.TicketCount,
            Archived = space.ArchivedAt is null ? (bool?)null : true,
        }));

    [McpServerTool(Name = "get_space_fields", ReadOnly = true), Description(
        "Lists the values the ticket fields of a space accept: ticket types, priorities, statuses, labels, epics, open sprints, " +
        "and the space's custom fields with their \"cf.\" keys, kinds, the ticket types that show them and their options.")]
    public Task<string> GetSpaceFields([Description("Space key.")] string space) => runner.ReadAsync(async () =>
    {
        var project = await resolver.SpaceAsync(space);
        var openSprints = project.Type == BoardType.Scrum
            ? (await sprints.GetBacklogAsync(project.Id)).Sprints.Select(item => $"{item.Sprint.Name} ({item.Sprint.State})").ToList()
            : null;
        var types = await resolver.TypesAsync(project.Id);
        return new
        {
            project.Key,
            project.Name,
            Board = project.Type.ToString(),
            Types = types.Select(type => new { type.Name, Level = type.Hierarchy.ToString() }),
            Priorities = (await tickets.GetPrioritiesAsync()).OrderBy(priority => priority.Order).Select(priority => priority.Name),
            Statuses = (await resolver.StatusesAsync(project.Id)).Select(status => new { status.Name, Category = status.Category.ToString() }),
            Labels = await tickets.GetLabelsAsync(project.Id),
            Epics = (await tickets.GetEpicsAsync(project.Id)).Select(epic => new { epic.Key, epic.Title, Status = epic.Status.Name }),
            Sprints = openSprints,
            CustomFields = await customFields.DescribeAsync(project.Id, types),
        };
    });

    [McpServerTool(Name = "find_users", ReadOnly = true), Description("Finds people by name or email, to assign or mention them.")]
    public Task<string> FindUsers([Description("Part of a name or email.")] string query) => runner.ReadAsync(async () =>
        (await users.GetSearchAsync(query)).Select(user => new { user.Name, user.Email, user.Id }));
}
