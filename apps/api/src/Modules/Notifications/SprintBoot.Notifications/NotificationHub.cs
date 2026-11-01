using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Security.Claims;

namespace SprintBoot.Notifications;

[Authorize]
public class NotificationHub : Hub
{
    public const string Path = "/hubs/notifications";
    public const string ChangedEvent = "NotificationsChanged";

    public static string Group(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(AbpClaimTypes.UserId)?.Value;
        if (Guid.TryParse(userId, out var id))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(id));
        }

        await base.OnConnectedAsync();
    }
}

public class NotificationPusher(IHubContext<NotificationHub> hub) : ITransientDependency
{
    /// <summary>A signal only; the list always comes from the API, so a missed push loses nothing.</summary>
    public async Task PushAsync(IEnumerable<Guid> userIds)
    {
        foreach (var userId in userIds)
        {
            await hub.Clients.Group(NotificationHub.Group(userId)).SendAsync(NotificationHub.ChangedEvent);
        }
    }
}
