using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace SprintBoot.Identity;

public class LinkTicket
{
    public Guid UserId { get; set; }

    public string Provider { get; set; } = string.Empty;
}

/// <summary>
/// Linking leaves the app for the IdP as a full page visit, which carries no bearer token. A
/// short-lived, single-use ticket made while signed in says who the new login belongs to.
/// </summary>
public class LinkedLoginService(
    IdentityUserManager userManager,
    IRepository<ExternalProviderConfig, Guid> providers,
    IRepository<AuthPolicy, Guid> policies,
    IDistributedCache<LinkTicket> tickets) : ITransientDependency
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);

    public async Task<List<LinkedLoginDto>> GetListAsync(IdentityUser user)
    {
        var enabled = await providers.GetListAsync(item => item.IsEnabled);
        var logins = await userManager.GetLoginsAsync(user);
        var canUnlink = logins.Count > 1 || await HasUsablePasswordAsync(user);

        var rows = enabled.OrderBy(item => item.DisplayName).Select(provider =>
        {
            var login = logins.FirstOrDefault(item => item.LoginProvider == provider.Name);
            return new LinkedLoginDto
            {
                Provider = provider.Name,
                DisplayName = provider.DisplayName,
                Type = provider.Type,
                IsLinked = login is not null,
                CanLink = login is null,
                CanUnlink = login is not null && canUnlink,
            };
        }).ToList();

        // A provider switched off since still shows, so the person can drop the stale link.
        rows.AddRange(logins.Where(login => enabled.All(item => item.Name != login.LoginProvider)).Select(login => new LinkedLoginDto
        {
            Provider = login.LoginProvider,
            DisplayName = login.ProviderDisplayName ?? login.LoginProvider,
            Type = null,
            IsLinked = true,
            CanLink = false,
            CanUnlink = canUnlink,
        }));
        return rows;
    }

    public async Task<string> CreateTicketAsync(IdentityUser user, string provider)
    {
        var config = await providers.FirstOrDefaultAsync(item => item.Name == provider && item.IsEnabled)
            ?? throw new BusinessException(SsoErrors.ExternalFailed);
        if ((await userManager.GetLoginsAsync(user)).Any(item => item.LoginProvider == config.Name))
        {
            throw new BusinessException(SsoErrors.AlreadyLinked);
        }

        var ticket = InvitationToken.NewToken();
        await tickets.SetAsync(ticket, new LinkTicket { UserId = user.Id, Provider = config.Name }, new() { AbsoluteExpirationRelativeToNow = TicketLifetime });
        return ticket;
    }

    /// <summary>Single use: the ticket is gone once read, whether the link then works or not.</summary>
    public async Task<LinkTicket?> RedeemAsync(string ticket)
    {
        var found = await tickets.GetAsync(ticket);
        if (found is not null)
        {
            await tickets.RemoveAsync(ticket);
        }

        return found;
    }

    public async Task LinkAsync(Guid userId, ExternalIdentity identity)
    {
        // One IdP account opens one Sprint Boot account; moving it would take over someone else's.
        var owner = await userManager.FindByLoginAsync(identity.Provider, identity.Subject);
        if (owner is not null)
        {
            throw new BusinessException(owner.Id == userId ? SsoErrors.AlreadyLinked : SsoErrors.LinkedElsewhere);
        }

        var user = await userManager.GetByIdAsync(userId);
        (await userManager.AddLoginAsync(user, new UserLoginInfo(identity.Provider, identity.Subject, identity.ProviderDisplayName))).CheckErrors();
    }

    public async Task UnlinkAsync(IdentityUser user, string provider)
    {
        var logins = await userManager.GetLoginsAsync(user);
        var login = logins.FirstOrDefault(item => item.LoginProvider == provider)
            ?? throw new BusinessException(SsoErrors.ExternalFailed);
        if (logins.Count == 1 && !await HasUsablePasswordAsync(user))
        {
            throw new BusinessException(SsoErrors.LastMethod);
        }

        (await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey)).CheckErrors();
    }

    /// <summary>A password only counts while password sign-in is open to this person.</summary>
    private async Task<bool> HasUsablePasswordAsync(IdentityUser user)
    {
        if (!await userManager.HasPasswordAsync(user))
        {
            return false;
        }

        var policy = await policies.FirstOrDefaultAsync();
        return policy is not { LocalLoginEnabled: false } || await userManager.IsInRoleAsync(user, SystemRoles.Admin);
    }
}
