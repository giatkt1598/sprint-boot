namespace SprintBoot.Projects;

/// <summary>Project-level permission keys. Always check a key, never a role name.</summary>
public static class ProjectPermissions
{
    public const string Manage = "Project.Manage";
    public const string ManageMembers = "Project.Members.Manage";
    public const string ViewTickets = "Ticket.View";
    public const string EditTickets = "Ticket.Edit";
    public const string DeleteAnyTicket = "Ticket.DeleteAny";
    public const string DeleteOwnTicket = "Ticket.DeleteOwn";
    public const string TransitionTickets = "Ticket.Transition";
    public const string RankBacklog = "Backlog.Rank";
    public const string ManageSprints = "Sprint.Manage";
    public const string DeleteAnyComment = "Comment.DeleteAny";
    public const string DeleteAnyWorkLog = "WorkLog.DeleteAny";
}

public static class ProjectRoleNames
{
    public const string Admin = "ProjectAdmin";
    public const string Member = "Member";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Admin, Member, Viewer];

    /// <summary>The fixed matrix of the core product. Stored per role so custom roles can come later.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultPermissions = new Dictionary<string, string[]>
    {
        [Viewer] = [ProjectPermissions.ViewTickets],
        [Member] =
        [
            ProjectPermissions.ViewTickets,
            ProjectPermissions.EditTickets,
            ProjectPermissions.DeleteOwnTicket,
            ProjectPermissions.TransitionTickets,
            ProjectPermissions.RankBacklog,
        ],
        [Admin] =
        [
            ProjectPermissions.ViewTickets,
            ProjectPermissions.EditTickets,
            ProjectPermissions.DeleteOwnTicket,
            ProjectPermissions.DeleteAnyTicket,
            ProjectPermissions.TransitionTickets,
            ProjectPermissions.RankBacklog,
            ProjectPermissions.ManageSprints,
            ProjectPermissions.Manage,
            ProjectPermissions.ManageMembers,
            ProjectPermissions.DeleteAnyComment,
            ProjectPermissions.DeleteAnyWorkLog,
        ],
    };
}
