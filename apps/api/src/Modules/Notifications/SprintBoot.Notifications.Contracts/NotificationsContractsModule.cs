using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.Notifications;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class NotificationsContractsModule : AbpModule
{
}
