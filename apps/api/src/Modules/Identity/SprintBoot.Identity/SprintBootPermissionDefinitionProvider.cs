using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace SprintBoot.Identity;

public class SprintBootPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(SprintBootPermissions.Group, new FixedLocalizableString("Sprint Boot"));
        group.AddPermission(SprintBootPermissions.ManageUsers, new FixedLocalizableString("Manage users"));
        group.AddPermission(SprintBootPermissions.RestoreTickets, new FixedLocalizableString("Restore deleted tickets"));
        group.AddPermission(SprintBootPermissions.ManageNotificationSchemes, new FixedLocalizableString("Manage notification schemes"));
        group.AddPermission(SprintBootPermissions.ManageSso, new FixedLocalizableString("Manage single sign-on"));
        group.AddPermission(SprintBootPermissions.ManageEmail, new FixedLocalizableString("Manage single sign-on"));
        group.AddPermission(SprintBootPermissions.ManageInvitations, new FixedLocalizableString("Manage all invitations"));
        group.AddPermission(SprintBootPermissions.ManageGit, new FixedLocalizableString("Manage Git connections"));
        group.AddPermission(SprintBootPermissions.ManageWorkflows, new FixedLocalizableString("Manage workflows"));
        group.AddPermission(SprintBootPermissions.ManageTicketLinkTypes, new FixedLocalizableString("Manage ticket link types"));
        group.AddPermission(SprintBootPermissions.ManageTicketTemplates, new FixedLocalizableString("Manage workspace ticket templates"));
        group.AddPermission(SprintBootPermissions.ManageCustomFields, new FixedLocalizableString("Manage workspace custom fields"));
        group.AddPermission(SprintBootPermissions.ManageAi, new FixedLocalizableString("Manage the AI assistant"));
        group.AddPermission(SprintBootPermissions.AccessAllProjects, new FixedLocalizableString("Access all projects"));
        group.AddPermission(SprintBootPermissions.DeleteProjects, new FixedLocalizableString("Delete spaces"));
        group.AddPermission(SprintBootPermissions.ManageDocs, new FixedLocalizableString("Manage the Docmost connection"));
    }
}
