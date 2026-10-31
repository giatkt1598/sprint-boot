using SprintBoot.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Modularity;

namespace SprintBoot.DbMigrator;

[DependsOn(typeof(AbpAutofacModule), typeof(SprintBootEntityFrameworkCoreModule))]
public class DbMigratorModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpBackgroundWorkerOptions>(options => options.IsEnabled = false);
    }
}
