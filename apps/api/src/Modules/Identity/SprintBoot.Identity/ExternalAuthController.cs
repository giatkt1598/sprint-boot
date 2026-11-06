using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.Uow;

namespace SprintBoot.Identity;

/// <summary>Under /connect because the session cookie it sets is scoped there.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("connect/external")]
[AllowAnonymous]
public class ExternalAuthController(
    ExternalSchemeRegistry registry,
    ExternalAccountLinker linker,
    AbpSignInManager signInManager,
    IRepository<ExternalProviderConfig, Guid> providers,
    LinkedLoginService linkedLogins,
    IUnitOfWorkManager unitOfWorkManager) : AbpController
{
    private const string InvitationItem = "invitation";
    private const string ReturnItem = "returnUrl";
    private const string ProviderItem = "provider";
    private const string LinkItem = "link";
    private const string ProfilePage = "/account/profile";

    [HttpGet("{provider}/challenge")]
    public async Task<IActionResult> ChallengeAsync(string provider, string? returnUrl, string? invitation, string? link)
    {
        if (!await registry.EnsureAsync(provider))
        {
            return Redirect($"/login?error={Code(SsoErrors.ExternalFailed)}");
        }

        var properties = new AuthenticationProperties { RedirectUri = "/connect/external/complete" };
        properties.Items[ProviderItem] = provider;
        properties.Items[ReturnItem] = returnUrl;
        if (!string.IsNullOrEmpty(invitation))
        {
            properties.Items[InvitationItem] = invitation;
        }

        if (!string.IsNullOrEmpty(link))
        {
            properties.Items[LinkItem] = link;
        }

        return Challenge(properties, ExternalSchemeRegistry.SchemeName(provider));
    }

    [HttpGet("complete")]
    public async Task<IActionResult> CompleteAsync()
    {
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (!result.Succeeded || result.Principal is null || result.Properties is null)
        {
            return Redirect($"/login?error={Code(SsoErrors.ExternalFailed)}");
        }

        var items = result.Properties.Items;
        var providerName = Item(items, ProviderItem) ?? string.Empty;
        var invitation = Item(items, InvitationItem);
        if (Item(items, LinkItem) is { } link)
        {
            return await CompleteLinkAsync(result.Principal, providerName, link);
        }

        try
        {
            using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var provider = await providers.GetAsync(item => item.Name == providerName);
            var user = await linker.ResolveAsync(ReadIdentity(result.Principal, provider), invitation);
            await signInManager.SignInAsync(user, isPersistent: false, authenticationMethod: providerName);
            await uow.CompleteAsync();
        }
        catch (BusinessException failure)
        {
            return Redirect($"/login?error={Code(failure.Code)}");
        }

        // Only our own authorize endpoint, never a redirect chosen by the caller.
        var returnUrl = Item(items, ReturnItem);
        return Redirect(returnUrl?.StartsWith("/connect/authorize", StringComparison.Ordinal) == true ? returnUrl : "/");
    }

    /// <summary>Adds the IdP login to the account that asked for it, without signing anyone in.</summary>
    private async Task<IActionResult> CompleteLinkAsync(ClaimsPrincipal principal, string providerName, string link)
    {
        try
        {
            using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var ticket = await linkedLogins.RedeemAsync(link);
            if (ticket is null || ticket.Provider != providerName)
            {
                throw new BusinessException(SsoErrors.ExternalFailed);
            }

            var provider = await providers.GetAsync(item => item.Name == providerName);
            await linkedLogins.LinkAsync(ticket.UserId, ReadIdentity(principal, provider));
            await uow.CompleteAsync();
        }
        catch (BusinessException failure)
        {
            return Redirect($"{ProfilePage}?linkError={Code(failure.Code)}");
        }

        return Redirect($"{ProfilePage}?linked={Uri.EscapeDataString(providerName)}");
    }

    public static ExternalIdentity ReadIdentity(ClaimsPrincipal principal, ExternalProviderConfig provider)
    {
        string? Claim(params string?[] types) =>
            types.Where(type => !string.IsNullOrEmpty(type)).Select(type => principal.FindFirst(type!)?.Value).FirstOrDefault(value => !string.IsNullOrEmpty(value));

        var subject = Claim("sub", ClaimTypes.NameIdentifier) ?? throw new BusinessException(SsoErrors.ExternalFailed);
        // SAML has no email_verified claim; the IdP is the organization's own, so its email is taken as verified.
        var verified = provider.Type == ExternalProviderType.Saml2
            || string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        return new ExternalIdentity(
            provider.Name,
            provider.DisplayName,
            subject,
            Claim(provider.EmailClaim, "email", ClaimTypes.Email, SamlEmailAttribute),
            verified,
            Claim(provider.NameClaim, "name", ClaimTypes.Name),
            provider.Type == ExternalProviderType.Google ? AvatarImporter.TrustedPicture(Claim("picture")) : null);
    }

    /// <summary>The attribute name many SAML IdPs use for email (the LDAP mail OID).</summary>
    private const string SamlEmailAttribute = "urn:oid:0.9.2342.19200300.100.1.3";

    private static string? Item(IDictionary<string, string?> items, string key) =>
        items.TryGetValue(key, out var value) ? value : null;

    private static string Code(string? code) => (code ?? SsoErrors.ExternalFailed).Split(':').Last();
}
