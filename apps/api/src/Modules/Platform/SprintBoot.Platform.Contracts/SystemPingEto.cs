using Volo.Abp.EventBus;

namespace SprintBoot.Platform;

/// <summary>Event mẫu để chứng minh luồng outbox, RabbitMQ, inbox, consumer.</summary>
[EventName("SprintBoot.Platform.SystemPing")]
public class SystemPingEto
{
    public Guid PingId { get; set; }
}
