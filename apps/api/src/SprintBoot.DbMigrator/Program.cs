using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SprintBoot.DbMigrator;
using Volo.Abp;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.Services.AddSerilog();
    builder.ConfigureContainer(builder.Services.AddAutofacServiceProviderFactory());
    await builder.Services.AddApplicationAsync<DbMigratorModule>();

    using var host = builder.Build();
    await host.InitializeAsync();
    await host.Services.GetRequiredService<DatabaseMigrator>().RunAsync();
    await host.Services.GetRequiredService<IAbpApplicationWithExternalServiceProvider>().ShutdownAsync();
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Migration failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
