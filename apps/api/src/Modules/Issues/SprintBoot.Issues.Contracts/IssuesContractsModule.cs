using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.Issues;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class IssuesContractsModule : AbpModule
{
}
