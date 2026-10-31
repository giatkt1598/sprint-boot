using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.GitIntegration;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class GitIntegrationContractsModule : AbpModule
{
}
