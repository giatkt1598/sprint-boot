using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SprintBoot.Workflows;

/// <summary>Sends stored webhooks with exponential backoff. Failures are logged and never touch the issue.</summary>
public class WebhookDeliveryWorker : AsyncPeriodicBackgroundWorkerBase
{
    public const int MaxAttempts = 5;

    private readonly IConfiguration _configuration;

    public WebhookDeliveryWorker(AbpAsyncTimer timer, IServiceScopeFactory scopeFactory, IConfiguration configuration)
        : base(timer, scopeFactory)
    {
        _configuration = configuration;
        timer.Period = configuration.GetValue("Workflows:Webhooks:PollMilliseconds", 5000);
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        await using var handle = await services.GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(nameof(WebhookDeliveryWorker));
        if (handle is null)
        {
            return;
        }

        var deliveries = services.GetRequiredService<IRepository<WebhookDelivery, Guid>>();
        var clock = services.GetRequiredService<IClock>();
        var http = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(WebhookDeliveryWorker));
        var logger = services.GetRequiredService<ILogger<WebhookDeliveryWorker>>();
        var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>();
        var backoff = _configuration.GetValue("Workflows:Webhooks:BackoffSeconds", 30d);

        using var uow = unitOfWork.Begin(requiresNew: true);
        var now = clock.Now;
        var due = await deliveries.GetListAsync(delivery =>
            delivery.State == WebhookDeliveryState.Pending && delivery.NextAttemptAt <= now);

        foreach (var delivery in due.Take(50))
        {
            delivery.Attempts++;
            try
            {
                using var content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await http.PostAsync(delivery.Url, content, timeout.Token);
                response.EnsureSuccessStatusCode();
                delivery.State = WebhookDeliveryState.Delivered;
                delivery.LastError = null;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                delivery.LastError = exception.Message[..Math.Min(exception.Message.Length, 500)];
                logger.LogWarning(exception, "Webhook {DeliveryId} failed on attempt {Attempt}", delivery.Id, delivery.Attempts);
                if (delivery.Attempts >= MaxAttempts)
                {
                    delivery.State = WebhookDeliveryState.Failed;
                }
                else
                {
                    delivery.NextAttemptAt = clock.Now.AddSeconds(backoff * Math.Pow(2, delivery.Attempts - 1));
                }
            }

            await deliveries.UpdateAsync(delivery);
        }

        await uow.CompleteAsync();
    }
}

/// <summary>Moves issues of queued migrations in batches, each in its own transaction.</summary>
public class WorkflowMigrationWorker : AsyncPeriodicBackgroundWorkerBase
{
    public WorkflowMigrationWorker(AbpAsyncTimer timer, IServiceScopeFactory scopeFactory, IConfiguration configuration)
        : base(timer, scopeFactory)
    {
        timer.Period = configuration.GetValue("Workflows:Migration:PollMilliseconds", 5000);
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        await using var handle = await services.GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(nameof(WorkflowMigrationWorker));
        if (handle is null)
        {
            return;
        }

        var migrations = services.GetRequiredService<IRepository<WorkflowMigration, Guid>>();
        var issues = services.GetRequiredService<IWorkflowIssueGateway>();
        var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>();

        using var uow = unitOfWork.Begin(requiresNew: true);
        var migration = await migrations.FirstOrDefaultAsync(item => item.State == MigrationState.Pending);
        if (migration is null)
        {
            return;
        }

        var scope = JsonSerializer.Deserialize<IssueScope>(migration.ScopeJson)!;
        var mappings = JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(migration.MappingJson)!;
        var moved = await issues.MigrateBatchAsync(scope, migration.ToVersionId, mappings, IssueMigrator.BatchSize);
        migration.Processed += moved;
        if (moved == 0)
        {
            migration.State = MigrationState.Completed;
        }

        await migrations.UpdateAsync(migration);
        await uow.CompleteAsync();
    }
}
