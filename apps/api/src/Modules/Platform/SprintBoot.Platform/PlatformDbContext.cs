using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace SprintBoot.Platform;

[ConnectionStringName("Default")]
public interface IPlatformDbContext : IEfCoreDbContext
{
    DbSet<SystemPingReceipt> SystemPingReceipts { get; }
}

[ConnectionStringName("Default")]
public class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
    : AbpDbContext<PlatformDbContext>(options), IPlatformDbContext
{
    public DbSet<SystemPingReceipt> SystemPingReceipts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigurePlatform();
    }
}

public static class PlatformDbContextModelBuilderExtensions
{
    public const string Schema = "platform";

    public static void ConfigurePlatform(this ModelBuilder builder)
    {
        builder.Entity<SystemPingReceipt>(b =>
        {
            b.ToTable("system_ping_receipts", Schema);
        });
    }
}
