namespace SprintBoot.Identity;

/// <summary>System-level permission keys. Code checks these keys, never role names.</summary>
public static class SprintBootPermissions
{
    public const string Group = "SprintBoot";
    public const string ManageUsers = Group + ".Users.Manage";
    public const string RestoreTickets = Group + ".Tickets.Restore";
    public const string ManageNotificationSchemes = Group + ".NotificationSchemes.Manage";
    public const string ManageSso = Group + ".Sso.Manage";
    public const string ManageEmail = Group + ".Email.Manage";
    public const string ManageInvitations = Group + ".Invitations.Manage";
    public const string ManageGit = Group + ".Git.Manage";
    public const string ManageWorkflows = Group + ".Workflows.Manage";
    public const string ManageTicketLinkTypes = Group + ".TicketLinkTypes.Manage";
    public const string ManageTicketTemplates = Group + ".TicketTemplates.Manage";
    public const string ManageAi = Group + ".Ai.Manage";
    public const string AccessAllProjects = Group + ".Projects.AccessAll";

    /// <summary>Delete archived spaces, and restore them from the trash.</summary>
    public const string DeleteProjects = Group + ".Projects.Delete";

    /// <summary>Connect Sprint Boot to Docmost.</summary>
    public const string ManageDocs = Group + ".Docs.Manage";
}
