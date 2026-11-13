using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Application;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace SprintBoot.DocsIntegration;

[DependsOn(
    typeof(DocsIntegrationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule))]
public class DocsIntegrationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context) =>
        context.Services.AddHttpClient(DocmostClient.ClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
}
