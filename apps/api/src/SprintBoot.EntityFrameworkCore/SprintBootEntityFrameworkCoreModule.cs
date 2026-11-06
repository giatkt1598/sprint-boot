using Microsoft.Extensions.DependencyInjection;
using SprintBoot.Agile;
using SprintBoot.DocsIntegration;
using SprintBoot.GitIntegration;
using SprintBoot.Identity;
using SprintBoot.Issues;
using SprintBoot.Assistant;
using SprintBoot.Notifications;
using SprintBoot.Platform;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.Timing;

namespace SprintBoot.EntityFrameworkCore;

[DependsOn(
    typeof(AbpEntityFrameworkCorePostgreSqlModule),
    typeof(AbpIdentityEntityFrameworkCoreModule),
    typeof(AbpOpenIddictEntityFrameworkCoreModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule),
    typeof(AbpSettingManagementEntityFrameworkCoreModule),
    typeof(AbpAuditLoggingEntityFrameworkCoreModule),
    typeof(IdentityModule),
    typeof(PlatformModule),
    typeof(ProjectsModule),
    typeof(IssuesModule),
    typeof(WorkflowsModule),
    typeof(AgileModule),
    typeof(GitIntegrationModule),
    typeof(NotificationsModule),
    typeof(AssistantModule),
    typeof(DocsIntegrationModule))]
public class SprintBootEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<SprintBootDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        Configure<AbpDbContextOptions>(options => options.UseNpgsql());

        // Npgsql only accepts UTC values for timestamp with time zone.
        Configure<AbpClockOptions>(options => options.Kind = DateTimeKind.Utc);

        Configure<AbpDistributedEventBusOptions>(options =>
        {
            options.Outboxes.Configure(config => config.UseDbContext<SprintBootDbContext>());
            options.Inboxes.Configure(config => config.UseDbContext<SprintBootDbContext>());
        });

        // One failing event must not stall every event behind it.
        Configure<AbpEventBusBoxesOptions>(options => options.InboxProcessorFailurePolicy = InboxProcessorFailurePolicy.RetryLater);
    }
}
