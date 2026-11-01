using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi;
using SprintBoot.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using SprintBoot.Host.HealthChecks;
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
using Volo.Abp.OpenIddict;
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
    private const string ReadyTag = "ready";

    /// <summary>Assemblies exposing versioned APIs. Register each new module here.</summary>
    private static readonly System.Reflection.Assembly[] ApiAssemblies =
    [
        typeof(PlatformModule).Assembly,
        typeof(IdentityModule).Assembly,
        typeof(ProjectsModule).Assembly,
        typeof(WorkflowsModule).Assembly,
        typeof(IssuesModule).Assembly,
    ];

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var role = configuration.GetRuntimeRole();

        ValidateStorage(configuration);

        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            foreach (var assembly in ApiAssemblies)
            {
                options.ConventionalControllers.Create(assembly, setting => setting.RootPath = ApiRoutes.RootPath);
            }
        });

        // JSON only, which also keeps text/plain out of the OpenAPI document.
        Configure<MvcOptions>(options => options.OutputFormatters.RemoveType<StringOutputFormatter>());

        Configure<AbpDistributedCacheOptions>(options => options.KeyPrefix = "SprintBoot:");

        Configure<AbpBackgroundWorkerOptions>(options => options.IsEnabled = role != RuntimeRole.Api);

        var redis = ConnectionMultiplexer.Connect(configuration["Redis:Configuration"]
            ?? throw new InvalidOperationException("Missing configuration: Redis:Configuration."));
        context.Services.AddSingleton<IConnectionMultiplexer>(redis);
        context.Services.AddSingleton<IDistributedLockProvider>(
            _ => new RedisDistributedSynchronizationProvider(redis.GetDatabase()));

        context.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres", tags: [ReadyTag])
            .AddCheck<RedisHealthCheck>("redis", tags: [ReadyTag])
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [ReadyTag]);

        context.Services.AddAbpSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Sprint Boot API", Version = "v1" });
            options.DocInclusionPredicate((_, description) =>
                description.RelativePath?.StartsWith(ApiRoutes.Prefix, StringComparison.Ordinal) == true);
            options.CustomSchemaIds(SchemaId);
        });
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var role = context.ServiceProvider.GetRequiredService<IConfiguration>().GetRuntimeRole();

        app.UseCorrelationId();
        app.UseRouting();
        app.UseAbpSerilogEnrichers();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();
        app.UseUnitOfWork();
        app.UseAuthorization();

        if (role != RuntimeRole.Worker)
        {
            app.UseSwagger();
            app.UseAbpSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Sprint Boot API"));
        }

        app.UseConfiguredEndpoints(endpoints =>
        {
            endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
            endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(ReadyTag),
            });
        });
    }

    private static string SchemaId(Type type) => type.IsGenericType
        ? $"{type.Name[..type.Name.IndexOf('`')]}Of{string.Concat(type.GetGenericArguments().Select(SchemaId))}"
        : type.Name;

    private static void ValidateStorage(IConfiguration configuration)
    {
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var missing = storage.GetMissingKeys();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Missing storage configuration: {string.Join(", ", missing)}.");
        }
    }
}

public static class ApiRoutes
{
    public const string RootPath = "v1";
    public const string Prefix = "api/v1/";
}
