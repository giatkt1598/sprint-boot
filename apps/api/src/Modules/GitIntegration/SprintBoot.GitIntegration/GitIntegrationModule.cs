using Volo.Abp.Application;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace SprintBoot.GitIntegration;

[DependsOn(
    typeof(GitIntegrationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule))]
public class GitIntegrationModule : AbpModule
{
}
