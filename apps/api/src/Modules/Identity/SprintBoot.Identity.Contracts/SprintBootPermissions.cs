namespace SprintBoot.Identity;

/// <summary>System-level permission keys. Code checks these keys, never role names.</summary>
public static class SprintBootPermissions
{
    public const string Group = "SprintBoot";
    public const string ManageUsers = Group + ".Users.Manage";
    public const string RestoreIssues = Group + ".Issues.Restore";
    public const string ManageNotificationSchemes = Group + ".NotificationSchemes.Manage";
    public const string ManageSso = Group + ".Sso.Manage";
    public const string ManageEmail = Group + ".Email.Manage";
    public const string ManageInvitations = Group + ".Invitations.Manage";
    public const string ManageGit = Group + ".Git.Manage";
    public const string ManageWorkflows = Group + ".Workflows.Manage";
    public const string ManageIssueLinkTypes = Group + ".IssueLinkTypes.Manage";
    public const string ManageIssueTemplates = Group + ".IssueTemplates.Manage";
    public const string AccessAllProjects = Group + ".Projects.AccessAll";
}
