using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace SprintBoot.Platform;

/// <summary>Không lộ qua HTTP: GĐ0 chưa có xác thực nên không mở endpoint ghi.</summary>
public class SystemPingPublisher(IDistributedEventBus eventBus) : ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task<Guid> PublishAsync()
    {
        var pingId = Guid.NewGuid();
        await eventBus.PublishAsync(new SystemPingEto { PingId = pingId }, useOutbox: true);
        return pingId;
    }
}
