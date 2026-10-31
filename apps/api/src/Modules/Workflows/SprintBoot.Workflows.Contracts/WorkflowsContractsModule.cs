using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.Workflows;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class WorkflowsContractsModule : AbpModule
{
}
