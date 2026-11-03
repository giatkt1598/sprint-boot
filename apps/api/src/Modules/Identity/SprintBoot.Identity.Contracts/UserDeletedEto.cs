using Volo.Abp.EventBus;

namespace SprintBoot.Identity;

/// <summary>Other modules drop what only made sense for a living account, such as memberships.</summary>
[EventName("SprintBoot.Identity.UserDeleted")]
public class UserDeletedEto
{
    public Guid EventId { get; set; }

    public Guid UserId { get; set; }
}
