using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SprintBoot.Platform;

public class SystemPingHandler(IRepository<SystemPingReceipt, Guid> receipts, IClock clock)
    : IDistributedEventHandler<SystemPingEto>, ITransientDependency
{
    [UnitOfWork]
    public virtual async Task HandleEventAsync(SystemPingEto eventData)
    {
        if (await receipts.FindAsync(eventData.PingId) is null)
        {
            await receipts.InsertAsync(new SystemPingReceipt(eventData.PingId, clock.Now));
        }
    }
}
