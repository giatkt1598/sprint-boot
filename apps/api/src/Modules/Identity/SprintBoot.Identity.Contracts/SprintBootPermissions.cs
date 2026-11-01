namespace SprintBoot.Identity;

/// <summary>System-level permission keys. Code checks these keys, never role names.</summary>
public static class SprintBootPermissions
{
    public const string Group = "SprintBoot";
    public const string ManageUsers = Group + ".Users.Manage";
    public const string RestoreIssues = Group + ".Issues.Restore";
    public const string AccessAllProjects = Group + ".Projects.AccessAll";
}
