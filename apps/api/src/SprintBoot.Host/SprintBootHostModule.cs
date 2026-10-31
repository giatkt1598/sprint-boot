using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.OpenApi;
using SprintBoot.EntityFrameworkCore;
using SprintBoot.Platform;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Caching;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EventBus.RabbitMq;
using Volo.Abp.Modularity;
using Volo.Abp.Swashbuckle;

namespace SprintBoot.Host;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMvcModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpCachingStackExchangeRedisModule),
    typeof(AbpDistributedLockingModule),
    typeof(AbpEventBusRabbitMqModule),
    typeof(SprintBootEntityFrameworkCoreModule))]
public class SprintBootHostModule : AbpModule
{
    /// <summary>Mọi API nghiệp vụ có version. Module mới thêm assembly vào đây.</summary>
    private static readonly System.Reflection.Assembly[] ApiAssemblies =
    [
        typeof(PlatformModule).Assembly,
    ];

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var role = configuration.GetRuntimeRole();

        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            foreach (var assembly in ApiAssemblies)
            {
                options.ConventionalControllers.Create(assembly, setting => setting.RootPath = ApiRoutes.RootPath);
            }
        });

        Configure<AbpDistributedCacheOptions>(options => options.KeyPrefix = "SprintBoot:");

        Configure<AbpBackgroundWorkerOptions>(options => options.IsEnabled = role != RuntimeRole.Api);

        var redis = ConnectionMultiplexer.Connect(configuration["Redis:Configuration"]
            ?? throw new InvalidOperationException("Thiếu cấu hình Redis:Configuration."));
        context.Services.AddSingleton<IConnectionMultiplexer>(redis);
        context.Services.AddSingleton<IDistributedLockProvider>(
            _ => new RedisDistributedSynchronizationProvider(redis.GetDatabase()));

        context.Services.AddAbpSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Sprint Boot API", Version = "v1" });
            options.DocInclusionPredicate((_, description) =>
                description.RelativePath?.StartsWith(ApiRoutes.Prefix, StringComparison.Ordinal) == true);
            options.CustomSchemaIds(type => type.Name);
        });
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var role = context.ServiceProvider.GetRequiredService<IConfiguration>().GetRuntimeRole();

        app.UseCorrelationId();
        app.UseRouting();
        app.UseAbpSerilogEnrichers();
        app.UseUnitOfWork();

        if (role != RuntimeRole.Worker)
        {
            app.UseSwagger();
            app.UseAbpSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Sprint Boot API"));
            app.UseConfiguredEndpoints();
        }
    }
}

public static class ApiRoutes
{
    public const string RootPath = "v1";
    public const string Prefix = "api/v1/";
}
