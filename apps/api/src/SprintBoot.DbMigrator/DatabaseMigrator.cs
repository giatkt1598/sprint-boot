using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SprintBoot.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace SprintBoot.DbMigrator;

public class DatabaseMigrator(
    IConfiguration configuration,
    IDataSeeder dataSeeder,
    IDbContextProvider<SprintBootDbContext> dbContextProvider,
    IUnitOfWorkManager unitOfWorkManager,
    ILogger<DatabaseMigrator> logger) : ITransientDependency
{
    public async Task RunAsync()
    {
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var dbContext = await dbContextProvider.GetDbContextAsync();
            await dbContext.Database.MigrateAsync();
            await uow.CompleteAsync();
        }

        logger.LogInformation("Đã chạy migration");

        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Thiếu cấu hình Seed:AdminPassword.");
        }

        await dataSeeder.SeedAsync(new DataSeedContext()
            .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName, configuration["Seed:AdminEmail"])
            .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName, password));

        logger.LogInformation("Đã seed dữ liệu");
    }
}
