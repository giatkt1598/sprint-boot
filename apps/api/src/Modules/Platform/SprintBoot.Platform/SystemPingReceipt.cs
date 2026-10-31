using Volo.Abp.Domain.Entities;

namespace SprintBoot.Platform;

public class SystemPingReceipt : Entity<Guid>
{
    public DateTime ReceivedAt { get; private set; }

    protected SystemPingReceipt()
    {
    }

    public SystemPingReceipt(Guid pingId, DateTime receivedAt)
        : base(pingId)
    {
        ReceivedAt = receivedAt;
    }
}
