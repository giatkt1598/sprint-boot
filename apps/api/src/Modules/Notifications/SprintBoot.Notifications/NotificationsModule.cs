using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace SprintBoot.Notifications;

[DependsOn(
    typeof(NotificationsContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule),
    typeof(AbpBackgroundWorkersModule))]
public class NotificationsModule : AbpModule
{
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context) =>
        context.AddBackgroundWorkerAsync<NotificationCleanupWorker>();
}
