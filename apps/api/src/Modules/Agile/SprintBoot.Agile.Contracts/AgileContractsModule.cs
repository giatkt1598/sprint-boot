using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.Agile;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class AgileContractsModule : AbpModule
{
}
