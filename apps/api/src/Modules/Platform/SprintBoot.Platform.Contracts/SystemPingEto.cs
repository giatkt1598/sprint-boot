using Volo.Abp.EventBus;

namespace SprintBoot.Platform;

/// <summary>Sample event proving the outbox, RabbitMQ, inbox and consumer path.</summary>
[EventName("SprintBoot.Platform.SystemPing")]
public class SystemPingEto
{
    public Guid PingId { get; set; }
}
