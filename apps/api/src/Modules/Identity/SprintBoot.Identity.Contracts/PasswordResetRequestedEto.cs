using Volo.Abp.EventBus;

namespace SprintBoot.Identity;

/// <summary>Carries no token: the consumer generates it, so secrets never reach the outbox.</summary>
[EventName("SprintBoot.Identity.PasswordResetRequested")]
public class PasswordResetRequestedEto
{
    public Guid UserId { get; set; }

    public string Language { get; set; } = "en";
}
