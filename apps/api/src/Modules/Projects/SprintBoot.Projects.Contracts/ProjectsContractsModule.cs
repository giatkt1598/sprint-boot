using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.Projects;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class ProjectsContractsModule : AbpModule
{
}
