using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.Platform;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class PlatformContractsModule : AbpModule
{
}
