using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public const int MigrationSyncLimit = 3;
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin123!test";

    private const string RabbitUser = "test";
    private const string RabbitPassword = "test";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public const string GitHubWebhookSecret = "github-webhook-secret";

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
        Environment.SetEnvironmentVariable("Workflows__Migration__SyncLimit", MigrationSyncLimit.ToString());
        Environment.SetEnvironmentVariable("Workflows__Migration__PollMilliseconds", "300");
        Environment.SetEnvironmentVariable("Workflows__Webhooks__PollMilliseconds", "300");
        Environment.SetEnvironmentVariable("Workflows__Webhooks__BackoffSeconds", "1");
        Environment.SetEnvironmentVariable("Issues__Attachments__MaxFileSizeMb", "1");
        Environment.SetEnvironmentVariable("Storage__Local__Path", Path.Combine(Path.GetTempPath(), $"sb-test-{Guid.NewGuid():N}"));
        using (var rsa = System.Security.Cryptography.RSA.Create(2048))
        {
            Environment.SetEnvironmentVariable("Git__GitHub__PrivateKey", rsa.ExportRSAPrivateKeyPem());
        }

        Environment.SetEnvironmentVariable("Git__GitHub__AppId", "1");
        Environment.SetEnvironmentVariable("Git__GitHub__ApiUrl", FakeGitServer.GitHubApi);
        Environment.SetEnvironmentVariable("Git__GitHub__WebhookSecret", GitHubWebhookSecret);
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
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureTestServices(services =>
                {
                    services.Replace(ServiceDescriptor.Transient<Volo.Abp.Emailing.IEmailSender, ScriptedEmailSender>());
                    services.AddHttpClient("git").ConfigurePrimaryHttpMessageHandler(() => FakeGitServer.Instance);
                });
            });
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

    public async Task InstallGitHubAppAsync(Guid connectionId, long installationId)
    {
        using var scope = Factory.Services.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var connections = scope.ServiceProvider.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<SprintBoot.GitIntegration.GitConnection, Guid>>();
        var connection = await connections.GetAsync(connectionId);
        connection.InstallationId = installationId;
        await connections.UpdateAsync(connection);
        await uow.CompleteAsync();
    }

    public async Task ExpireInvitationAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var invitations = scope.ServiceProvider.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<SprintBoot.Identity.Invitation, Guid>>();
        var invitation = await invitations.GetAsync(id);
        invitation.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await invitations.UpdateAsync(invitation);
        await uow.CompleteAsync();
    }

    public async Task RunNotificationAsync(SprintBoot.Notifications.NotificationEvent item)
    {
        using var scope = Factory.Services.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        await scope.ServiceProvider.GetRequiredService<SprintBoot.Notifications.NotificationProcessor>().ProcessAsync(item);
        await uow.CompleteAsync();
    }

    public async Task<Dictionary<Guid, string>> GetRanksAsync(Guid projectId)
    {
        using var scope = Factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var uow = unitOfWork.Begin(requiresNew: true);
        var issues = scope.ServiceProvider.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<SprintBoot.Issues.Issue, Guid>>();
        return (await issues.GetListAsync(issue => issue.ProjectId == projectId)).ToDictionary(issue => issue.Id, issue => issue.Rank);
    }

    public HubConnection CreateBoardConnection(AuthClient client) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "hubs/board"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(client.AccessToken);
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

    public async Task<Guid> FindTransitionIdAsync(Guid projectId, Guid typeId, string name)
    {
        using var scope = Factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var uow = unitOfWork.Begin(requiresNew: true);
        var runtime = scope.ServiceProvider.GetRequiredService<SprintBoot.Workflows.IWorkflowRuntime>();
        var state = await runtime.GetInitialStateAsync(projectId, typeId);
        var transitions = scope.ServiceProvider.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<SprintBoot.Workflows.Transition, Guid>>();
        return (await transitions.GetListAsync(item => item.VersionId == state.VersionId)).Single(item => item.Name == name).Id;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await RabbitMq.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public static int GetFreePort()
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
