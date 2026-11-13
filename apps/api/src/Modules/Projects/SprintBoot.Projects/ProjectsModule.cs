using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace SprintBoot.Projects;

[DependsOn(
    typeof(ProjectsContractsModule),
    typeof(Volo.Abp.BlobStoring.AbpBlobStoringModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule),
    typeof(AbpBackgroundWorkersModule))]
public class ProjectsModule : AbpModule
{
    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context) =>
        await context.AddBackgroundWorkerAsync<ProjectPurgeWorker>();
}
