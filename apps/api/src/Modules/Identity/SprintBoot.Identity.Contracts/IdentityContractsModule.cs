using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.Identity;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class IdentityContractsModule : AbpModule
{
}
