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
    IUnitOfWorkManager unitOfWorkManager) : AbpController
{
    private const string InvitationItem = "invitation";
    private const string ReturnItem = "returnUrl";
    private const string ProviderItem = "provider";

    [HttpGet("{provider}/challenge")]
    public async Task<IActionResult> ChallengeAsync(string provider, string? returnUrl, string? invitation)
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

    public static ExternalIdentity ReadIdentity(ClaimsPrincipal principal, ExternalProviderConfig provider)
    {
        string? Claim(params string?[] types) =>
            types.Where(type => !string.IsNullOrEmpty(type)).Select(type => principal.FindFirst(type!)?.Value).FirstOrDefault(value => !string.IsNullOrEmpty(value));

        var subject = Claim("sub", ClaimTypes.NameIdentifier) ?? throw new BusinessException(SsoErrors.ExternalFailed);
        var verified = string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        return new ExternalIdentity(
            provider.Name,
            provider.DisplayName,
            subject,
            Claim(provider.EmailClaim, "email", ClaimTypes.Email),
            verified,
            Claim(provider.NameClaim, "name", ClaimTypes.Name));
    }

    private static string? Item(IDictionary<string, string?> items, string key) =>
        items.TryGetValue(key, out var value) ? value : null;

    private static string Code(string? code) => (code ?? SsoErrors.ExternalFailed).Split(':').Last();
}
