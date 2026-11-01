using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SprintBoot.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Runs the real host against real PostgreSQL, Redis and RabbitMQ containers.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin123!test";

    private const string RabbitUser = "test";
    private const string RabbitPassword = "test";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public RabbitMqContainer RabbitMq { get; private set; } = null!;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // A fixed host port keeps the address stable when the broker is restarted in a test.
        var rabbitPort = GetFreePort();
        RabbitMq = new RabbitMqBuilder("rabbitmq:4-alpine")
            .WithUsername(RabbitUser)
            .WithPassword(RabbitPassword)
            .WithPortBinding(rabbitPort, 5672)
            .Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), RabbitMq.StartAsync());

        // ABP reads configuration while the builder is created, before factory hooks run.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Redis__Configuration", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("Auth__RequireHttps", "false");
        Environment.SetEnvironmentVariable("RabbitMQ__Connections__Default__HostName", "localhost");
        Environment.SetEnvironmentVariable("RabbitMQ__Connections__Default__Port", rabbitPort.ToString());
        Environment.SetEnvironmentVariable("RabbitMQ__Connections__Default__UserName", RabbitUser);
        Environment.SetEnvironmentVariable("RabbitMQ__Connections__Default__Password", RabbitPassword);

        var options = new DbContextOptionsBuilder<SprintBootDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var dbContext = new SprintBootDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
        }

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        _ = Factory.Server;

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDataSeeder>().SeedAsync(new DataSeedContext()
            .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName, AdminEmail)
            .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName, AdminPassword));
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await RabbitMq.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
