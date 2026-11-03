using Volo.Abp.Application;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace SprintBoot.Projects;

[DependsOn(
    typeof(ProjectsContractsModule),
    typeof(Volo.Abp.BlobStoring.AbpBlobStoringModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule))]
public class ProjectsModule : AbpModule
{
}
