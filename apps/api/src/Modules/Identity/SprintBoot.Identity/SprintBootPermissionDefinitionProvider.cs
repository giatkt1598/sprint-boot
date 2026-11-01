using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace SprintBoot.Identity;

public class SprintBootPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(SprintBootPermissions.Group, new FixedLocalizableString("Sprint Boot"));
        group.AddPermission(SprintBootPermissions.ManageUsers, new FixedLocalizableString("Manage users"));
        group.AddPermission(SprintBootPermissions.RestoreIssues, new FixedLocalizableString("Restore deleted issues"));
        group.AddPermission(SprintBootPermissions.ManageWorkflows, new FixedLocalizableString("Manage workflows"));
        group.AddPermission(SprintBootPermissions.AccessAllProjects, new FixedLocalizableString("Access all projects"));
    }
}
