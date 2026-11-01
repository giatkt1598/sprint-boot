using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace SprintBoot.Workflows;

[DependsOn(
    typeof(WorkflowsContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule),
    typeof(AbpBackgroundWorkersModule))]
public class WorkflowsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddHttpClient();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        await context.AddBackgroundWorkerAsync<WebhookDeliveryWorker>();
        await context.AddBackgroundWorkerAsync<WorkflowMigrationWorker>();
    }
}
