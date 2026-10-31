using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.DocsIntegration;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class DocsIntegrationContractsModule : AbpModule
{
}
