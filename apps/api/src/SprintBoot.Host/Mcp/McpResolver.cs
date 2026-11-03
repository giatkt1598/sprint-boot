using System.Globalization;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using SprintBoot.Agile;
using SprintBoot.Identity;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Users;

namespace SprintBoot.Host.Mcp;

/// <summary>Where a sprint target leads: a sprint, or the backlog when the id is null.</summary>
public record SprintTarget(Guid? Id, string Name);

/// <summary>Turns the names an agent uses (space keys, type names, emails) into ids, within one call.</summary>
public partial class McpResolver(
    IProjectAppService projects,
    IIssueAppService issues,
    IStatusAppService statuses,
    IUserDirectoryAppService users,
    IUserDirectory directory,
    ISprintAppService sprints,
    ICurrentUser currentUser) : IScopedDependency
{
    private readonly Dictionary<string, ProjectDto> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, List<IssueTypeDto>> _types = [];
    private readonly Dictionary<string, UserRefDto> _users = new(StringComparer.OrdinalIgnoreCase);
    private List<PriorityDto>? _priorities;

    public async Task<ProjectDto> SpaceAsync(string key)
    {
        if (!_projects.TryGetValue(key.Trim(), out var project))
        {
            project = await projects.GetByKeyAsync(key.Trim());
            _projects[key.Trim()] = project;
        }

        return project;
    }

    public Task<IssueDto> IssueAsync(string key) => issues.GetByKeyAsync(key.Trim().ToUpperInvariant());

    public async Task<List<IssueTypeDto>> TypesAsync(Guid projectId)
    {
        if (!_types.TryGetValue(projectId, out var list))
        {
            list = await issues.GetTypesAsync(projectId);
            _types[projectId] = list;
        }

        return list;
    }

    public async Task<IssueTypeDto> TypeAsync(Guid projectId, string name) =>
        Pick(await TypesAsync(projectId), name, type => type.Name, "issue type");

    public async Task<PriorityDto> PriorityAsync(string name)
    {
        _priorities ??= await issues.GetPrioritiesAsync();
        return Pick(_priorities, name, priority => priority.Name, "priority");
    }

    public async Task<List<StatusDto>> StatusesAsync(Guid? projectId) => await statuses.GetListAsync(projectId);

    /// <summary>Accepts me, an id, an email, a name, or "Name &lt;email&gt;" as tools print people.</summary>
    public async Task<UserRefDto> UserAsync(string text)
    {
        var query = text.Trim();
        if (_users.TryGetValue(query, out var cached))
        {
            return cached;
        }

        if (query.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            var id = currentUser.GetId();
            return _users[query] = new UserRefDto { Id = id, Name = currentUser.Name ?? currentUser.UserName ?? "me", Email = currentUser.Email ?? string.Empty };
        }

        if (EmailInBrackets().Match(query) is { Success: true } bracket)
        {
            query = bracket.Groups[1].Value;
        }

        if (Guid.TryParse(query, out var userId))
        {
            return _users[text.Trim()] = (await directory.GetByIdsAsync([userId])).GetValueOrDefault(userId)
                ?? throw new McpException($"No user has the id {query}.");
        }

        var found = await users.GetSearchAsync(query);
        var match = found.FirstOrDefault(user => user.Email.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? Single(found.Where(user => user.Name.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList())
            ?? Single(found);
        if (match is null)
        {
            throw new McpException(found.Count == 0
                ? $"Nobody matches {text}. Use find_users."
                : $"{text} matches several people: {string.Join(", ", found.Take(10).Select(McpViews.Person))}. Use an email.");
        }

        return _users[text.Trim()] = match;
    }

    public async Task<MentionTarget?> MentionAsync(string text)
    {
        var user = await UserAsync(text);
        return new MentionTarget(user.Id, user.Name);
    }

    /// <summary>Accepts a sprint name or id, active for the running sprint, or backlog.</summary>
    public async Task<SprintTarget> SprintAsync(Guid projectId, string text)
    {
        var query = text.Trim();
        if (query.Equals("backlog", StringComparison.OrdinalIgnoreCase) || query.Length == 0)
        {
            return new SprintTarget(null, "Backlog");
        }

        var open = (await sprints.GetBacklogAsync(projectId)).Sprints.Select(item => item.Sprint).ToList();
        var sprint = query.Equals("active", StringComparison.OrdinalIgnoreCase)
            ? open.FirstOrDefault(item => item.State == SprintState.Active) ?? throw new McpException("This space has no active sprint.")
            : Guid.TryParse(query, out var id)
                ? open.FirstOrDefault(item => item.Id == id) ?? throw new McpException($"No open sprint has the id {query}.")
                : Pick(open, query, item => item.Name, "open sprint");
        return new SprintTarget(sprint.Id, sprint.Name);
    }

    public static DateOnly Date(string text, string field) =>
        DateOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var date)
            ? date
            : throw new McpException($"{field} must be a date like 2026-11-30.");

    public static DateTime Time(string text, string field) =>
        DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : throw new McpException($"{field} must be a date or time like 2026-11-30 or 2026-11-30T14:00:00Z.");

    /// <summary>Durations like 90, 45m, 1h 30m or 1.5h, in minutes.</summary>
    public static int Minutes(string text)
    {
        var query = text.Trim().ToLowerInvariant();
        if (int.TryParse(query, out var plain))
        {
            return plain;
        }

        var parts = DurationPart().Matches(query);
        if (parts.Count == 0 || string.Concat(parts.Select(part => part.Value)).Replace(" ", string.Empty) != query.Replace(" ", string.Empty))
        {
            throw new McpException($"{text} is not a duration. Use minutes, or forms like 45m, 1h 30m or 1.5h.");
        }

        var total = parts.Sum(part =>
            decimal.Parse(part.Groups[1].Value, CultureInfo.InvariantCulture) * (part.Groups[2].Value == "h" ? 60 : 1));
        return (int)Math.Round(total);
    }

    public static T Pick<T>(IEnumerable<T> items, string name, Func<T, string> nameOf, string what)
    {
        var list = items.ToList();
        return list.FirstOrDefault(item => string.Equals(nameOf(item), name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"No {what} is named {name}. Choose from: {string.Join(", ", list.Select(nameOf))}.");
    }

    private static UserRefDto? Single(List<UserRefDto> candidates) => candidates.Count == 1 ? candidates[0] : null;

    [GeneratedRegex(@"<([^<>@\s]+@[^<>\s]+)>")]
    private static partial Regex EmailInBrackets();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(h|m)")]
    private static partial Regex DurationPart();
}
