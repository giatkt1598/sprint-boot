using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SprintBoot.Platform;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OutboxTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<Guid> PublishAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SystemPingPublisher>().PublishAsync();
    }

    private async Task<bool> ReceiptExistsAsync(Guid pingId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true);
        var receipts = scope.ServiceProvider.GetRequiredService<IRepository<SystemPingReceipt, Guid>>();
        return await receipts.FindAsync(pingId) is not null;
    }

    private async Task WaitForReceiptAsync(Guid pingId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await ReceiptExistsAsync(pingId))
            {
                return;
            }

            await Task.Delay(500, Token);
        }

        throw new TimeoutException($"Event {pingId} did not reach the consumer within {timeout}.");
    }

    [Fact]
    public async Task Event_travels_through_outbox_and_broker_to_the_consumer()
    {
        var pingId = await PublishAsync();

        await WaitForReceiptAsync(pingId, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Event_published_while_the_broker_is_down_is_delivered_after_it_returns()
    {
        await fixture.RabbitMq.StopAsync(Token);

        var pingId = await PublishAsync();
        await Task.Delay(TimeSpan.FromSeconds(3), Token);
        (await ReceiptExistsAsync(pingId)).ShouldBeFalse();

        await fixture.RabbitMq.StartAsync(Token);

        await WaitForReceiptAsync(pingId, TimeSpan.FromSeconds(90));
    }
}
