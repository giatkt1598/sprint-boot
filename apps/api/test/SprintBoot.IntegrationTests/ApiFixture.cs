using Microsoft.AspNetCore.Identity;
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
using Volo.Abp.Uow;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

using IdentityUser = Volo.Abp.Identity.IdentityUser;

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

    public async Task<(CreateUserResult User, string Password)> CreateUserAsync(CancellationToken token)
    {
        const string password = "Member123!test";
        var email = $"user-{Guid.NewGuid():N}@test.local";
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IdentityUserManager>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var uow = unitOfWork.Begin(requiresNew: true);
        var user = new IdentityUser(Guid.NewGuid(), email, email) { Name = email };
        (await users.CreateAsync(user, password)).CheckErrors();
        (await users.AddToRoleAsync(user, "member")).CheckErrors();
        await uow.CompleteAsync(token);
        return (new CreateUserResult(user.Id, email), password);
    }

    public async Task<AuthClient> CreateUserClientAsync(CancellationToken token)
    {
        var (user, password) = await CreateUserAsync(token);
        var client = new AuthClient(this) { UserId = user.Id };
        await client.SignInAsync(user.Email, password, token);
        return client;
    }

    public async Task<string> GenerateResetTokenAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IdentityUserManager>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var uow = unitOfWork.Begin(requiresNew: true);
        return await users.GeneratePasswordResetTokenAsync(await users.GetByIdAsync(userId));
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

public record CreateUserResult(Guid Id, string Email);

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
