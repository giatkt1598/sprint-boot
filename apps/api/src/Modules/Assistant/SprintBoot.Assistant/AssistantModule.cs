using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace SprintBoot.Assistant;

[DependsOn(
    typeof(AssistantContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule),
    typeof(AbpSettingManagementDomainModule),
    typeof(AbpBackgroundWorkersModule))]
public class AssistantModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Answers stream for as long as the model writes.
        context.Services.AddHttpClient(ChatProviders.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(10));
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context) =>
        await context.AddBackgroundWorkerAsync<ConversationTitleWorker>();
}

public static class AssistantSettings
{
    public const string Enabled = "SprintBoot.Assistant.Enabled";
}

public class AssistantSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context) =>
        context.Add(new SettingDefinition(AssistantSettings.Enabled, "false", isVisibleToClients: false));
}
