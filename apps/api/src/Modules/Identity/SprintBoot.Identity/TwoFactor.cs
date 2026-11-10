using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace SprintBoot.Identity;

/// <summary>
/// The last step of every sign-in, by password, directory or identity provider: it signs the person in, or holds the
/// sign-in in Identity's two-factor cookie until they give a code or set up an authenticator app.
/// </summary>
public class TwoFactorGate(AbpSignInManager signInManager, IRepository<AuthPolicy, Guid> policies) : ITransientDependency
{
    public async Task<LoginOutcome> SignInAsync(IdentityUser user, string? authenticationMethod)
    {
        if (user.TwoFactorEnabled)
        {
            await HoldAsync(user, authenticationMethod);
            return LoginOutcome.TwoFactorCode;
        }

        if (await IsRequiredAsync())
        {
            await HoldAsync(user, authenticationMethod);
            return LoginOutcome.TwoFactorSetup;
        }

        await signInManager.SignInAsync(user, isPersistent: false, authenticationMethod);
        return LoginOutcome.Succeeded;
    }

    public async Task<bool> IsRequiredAsync() => (await policies.FirstOrDefaultAsync())?.RequireTwoFactor ?? false;

    /// <summary>The same cookie Identity sets when a password sign-in needs a second factor, which its code sign-in reads.</summary>
    private Task HoldAsync(IdentityUser user, string? authenticationMethod)
    {
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Id.ToString()));
        if (authenticationMethod is not null)
        {
            identity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, authenticationMethod));
        }

        return signInManager.Context.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>Ends a held sign-in once the person has set up their app, the way the code would have.</summary>
    public async Task CompleteHeldAsync(IdentityUser user)
    {
        var held = await signInManager.Context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        var method = held.Principal?.FindFirstValue(ClaimTypes.AuthenticationMethod);
        await signInManager.Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        List<Claim> claims = [new Claim("amr", "mfa")];
        if (method is not null)
        {
            claims.Add(new Claim(ClaimTypes.AuthenticationMethod, method));
        }

        await signInManager.SignInWithClaimsAsync(user, isPersistent: false, claims);
    }
}

/// <summary>
/// Authenticator apps (TOTP) and recovery codes, from ASP.NET Identity. The held endpoints serve a sign-in waiting for its
/// second step; the account ones serve a signed-in person. Exposed only through <see cref="AuthController"/>.
/// </summary>
[RemoteService(IsEnabled = false)]
public class TwoFactorAppService(
    AbpSignInManager signInManager,
    IdentityUserManager userManager,
    TwoFactorGate gate,
    UrlEncoder urlEncoder)
    : ApplicationService
{
    private const string Issuer = "Sprint Boot";
    private const int RecoveryCodeCount = 10;

    /// <summary>Reports a wrong code as a value: throwing would roll back the failed-attempt counter.</summary>
    [AllowAnonymous]
    public async Task<LoginOutcome> VerifyHeldAsync(TwoFactorCodeInput input)
    {
        // A sign-in held for setup has no code to give yet: it finishes by turning two-factor on.
        if (!(await HeldUserAsync()).TwoFactorEnabled)
        {
            throw new UserFriendlyException("Two-factor authentication is off", AuthErrors.TwoFactorNotEnabled);
        }

        var result = input.IsRecoveryCode
            ? await signInManager.TwoFactorRecoveryCodeSignInAsync(NormalizeRecovery(input.Code))
            : await signInManager.TwoFactorAuthenticatorSignInAsync(Normalize(input.Code), isPersistent: false, rememberClient: false);
        if (result.IsLockedOut)
        {
            return LoginOutcome.LockedOut;
        }

        return result.Succeeded ? LoginOutcome.Succeeded : LoginOutcome.InvalidCredentials;
    }

    [AllowAnonymous]
    public async Task<TwoFactorSetupDto> GetHeldSetupAsync() => await SetupOfAsync(await HeldUserAsync());

    /// <summary>Turns two-factor on for the held account and finishes its sign-in.</summary>
    [AllowAnonymous]
    public async Task<TwoFactorRecoveryCodesDto> CompleteHeldSetupAsync(TwoFactorCodeInput input)
    {
        var user = await HeldUserAsync();
        var codes = await EnableAsync(user, input.Code);
        await gate.CompleteHeldAsync(user);
        return codes;
    }

    [Authorize]
    public async Task<TwoFactorSetupDto> GetSetupAsync() => await SetupOfAsync(await CurrentAsync());

    [Authorize]
    public async Task<TwoFactorRecoveryCodesDto> CompleteSetupAsync(TwoFactorCodeInput input) => await EnableAsync(await CurrentAsync(), input.Code);

    [Authorize]
    public async Task<TwoFactorRecoveryCodesDto> RegenerateRecoveryCodesAsync(TwoFactorCodeInput input)
    {
        var user = await CurrentAsync();
        await CheckCodeAsync(user, input.Code);
        return new TwoFactorRecoveryCodesDto { RecoveryCodes = [.. await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? []] };
    }

    [Authorize]
    public async Task DisableAsync(TwoFactorCodeInput input)
    {
        if (await gate.IsRequiredAsync())
        {
            throw new UserFriendlyException("Two-factor authentication is required in this workspace", AuthErrors.TwoFactorRequiredByPolicy);
        }

        var user = await CurrentAsync();
        await CheckCodeAsync(user, input.Code);
        (await userManager.SetTwoFactorEnabledAsync(user, false)).CheckErrors();
        // A fresh key next time, so an app set up before stops working, and no recovery code left to use.
        (await userManager.ResetAuthenticatorKeyAsync(user)).CheckErrors();
        await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
    }

    private async Task<TwoFactorSetupDto> SetupOfAsync(IdentityUser user)
    {
        if (user.TwoFactorEnabled)
        {
            throw new UserFriendlyException("Two-factor authentication is already on", AuthErrors.TwoFactorAlreadyEnabled);
        }

        // The same key until setup finishes, so an app scanned once keeps working if the page reloads.
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            (await userManager.ResetAuthenticatorKeyAsync(user)).CheckErrors();
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var issuer = urlEncoder.Encode(Issuer);
        return new TwoFactorSetupDto
        {
            SharedKey = key!,
            AuthenticatorUri = $"otpauth://totp/{issuer}:{urlEncoder.Encode(user.Email)}?secret={key}&issuer={issuer}&digits=6",
        };
    }

    private async Task<TwoFactorRecoveryCodesDto> EnableAsync(IdentityUser user, string code)
    {
        if (user.TwoFactorEnabled)
        {
            throw new UserFriendlyException("Two-factor authentication is already on", AuthErrors.TwoFactorAlreadyEnabled);
        }

        if (!await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, Normalize(code)))
        {
            throw new UserFriendlyException("The code is not right", AuthErrors.TwoFactorCodeInvalid);
        }

        (await userManager.SetTwoFactorEnabledAsync(user, true)).CheckErrors();
        return new TwoFactorRecoveryCodesDto { RecoveryCodes = [.. await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? []] };
    }

    private async Task CheckCodeAsync(IdentityUser user, string code)
    {
        if (!user.TwoFactorEnabled)
        {
            throw new UserFriendlyException("Two-factor authentication is off", AuthErrors.TwoFactorNotEnabled);
        }

        if (!await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, Normalize(code)))
        {
            throw new UserFriendlyException("The code is not right", AuthErrors.TwoFactorCodeInvalid);
        }
    }

    private async Task<IdentityUser> HeldUserAsync() =>
        await signInManager.GetTwoFactorAuthenticationUserAsync()
        ?? throw new UserFriendlyException("The sign-in has expired", AuthErrors.TwoFactorExpired);

    private Task<IdentityUser> CurrentAsync() => userManager.GetByIdAsync(CurrentUser.GetId());

    /// <summary>People copy codes with the spaces or dashes their app shows.</summary>
    private static string Normalize(string code) => code.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

    /// <summary>Recovery codes are stored as XXXXX-XXXXX; people may type them in lower case or without the dash.</summary>
    private static string NormalizeRecovery(string code)
    {
        var plain = code.Replace(" ", string.Empty).Replace("-", string.Empty).Trim().ToUpperInvariant();
        return plain.Length == 10 ? $"{plain[..5]}-{plain[5..]}" : plain;
    }
}
