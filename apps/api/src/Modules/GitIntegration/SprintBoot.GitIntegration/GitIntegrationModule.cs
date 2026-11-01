using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace SprintBoot.GitIntegration;

[DependsOn(
    typeof(GitIntegrationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule),
    typeof(AbpBackgroundWorkersModule))]
public class GitIntegrationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddHttpClient(GitHttp.ClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context) =>
        context.AddBackgroundWorkerAsync<GitSyncWorker>();
}
