using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace SprintBoot.Identity;

/// <summary>Exposed only through <see cref="AuthController"/>, which adds CSRF validation.</summary>
[RemoteService(IsEnabled = false)]
public class AuthAppService(
    AbpSignInManager signInManager,
    IdentityUserManager userManager,
    IDistributedEventBus eventBus)
    : ApplicationService, IAuthAppService
{
    public const string LanguageProperty = "Language";

    /// <summary>Reports failures as a value: throwing would roll back the failed-attempt counter.</summary>
    [AllowAnonymous]
    public async Task<LoginOutcome> LoginAsync(LoginInput input)
    {
        var user = await userManager.FindByEmailAsync(input.Email);
        if (user is null)
        {
            return LoginOutcome.InvalidCredentials;
        }

        var result = await signInManager.PasswordSignInAsync(user, input.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return LoginOutcome.LockedOut;
        }

        return result.Succeeded ? LoginOutcome.Succeeded : LoginOutcome.InvalidCredentials;
    }

    [Authorize]
    public async Task<CurrentUserDto> GetMeAsync()
    {
        var user = await userManager.GetByIdAsync(CurrentUser.GetId());
        return await ToDtoAsync(user);
    }

    [AllowAnonymous]
    public async Task ForgotPasswordAsync(ForgotPasswordInput input)
    {
        var user = await userManager.FindByEmailAsync(input.Email);
        if (user is not null)
        {
            await eventBus.PublishAsync(
                new PasswordResetRequestedEto { UserId = user.Id, Language = input.Language },
                useOutbox: true);
        }
    }

    [AllowAnonymous]
    public async Task<ResetTokenStatusDto> ValidateResetTokenAsync(ValidateResetTokenInput input)
    {
        var user = await userManager.FindByIdAsync(input.UserId.ToString());
        var isValid = user is not null && await userManager.VerifyUserTokenAsync(
            user, userManager.Options.Tokens.PasswordResetTokenProvider, UserManager<IdentityUser>.ResetPasswordTokenPurpose, input.Token);
        return new ResetTokenStatusDto { IsValid = isValid };
    }

    [AllowAnonymous]
    public async Task ResetPasswordAsync(ResetPasswordInput input)
    {
        var user = await userManager.FindByIdAsync(input.UserId.ToString())
            ?? throw new UserFriendlyException("Reset link is invalid", AuthErrors.ResetLinkInvalid);

        var result = await userManager.ResetPasswordAsync(user, input.Token, input.NewPassword);
        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken)))
        {
            throw new UserFriendlyException("Reset link is invalid", AuthErrors.ResetLinkInvalid);
        }

        ThrowIfRejected(result);
    }

    [Authorize]
    public async Task ChangePasswordAsync(ChangePasswordInput input)
    {
        var user = await userManager.GetByIdAsync(CurrentUser.GetId());
        var result = await userManager.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);
        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            throw new UserFriendlyException("Current password is wrong", AuthErrors.CurrentPasswordInvalid);
        }

        ThrowIfRejected(result);
    }

    [Authorize]
    public async Task<CurrentUserDto> UpdateProfileAsync(UpdateProfileInput input)
    {
        var user = await userManager.GetByIdAsync(CurrentUser.GetId());
        user.Name = input.Name;
        user.SetProperty(LanguageProperty, input.Language);
        (await userManager.UpdateAsync(user)).CheckErrors();
        return await ToDtoAsync(user);
    }

    private static void ThrowIfRejected(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new UserFriendlyException(
                string.Join(" ", result.Errors.Select(error => error.Description)), AuthErrors.PasswordRejected);
        }
    }

    private async Task<CurrentUserDto> ToDtoAsync(IdentityUser user)
    {
        var permissions = new List<string>();
        foreach (var permission in new[] { SprintBootPermissions.ManageUsers, SprintBootPermissions.RestoreIssues, SprintBootPermissions.ManageWorkflows })
        {
            if (await AuthorizationService.IsGrantedAsync(permission))
            {
                permissions.Add(permission);
            }
        }

        return new CurrentUserDto
        {
            Id = user.Id,
            Email = user.Email,
            Name = string.IsNullOrWhiteSpace(user.Name) ? user.UserName : user.Name,
            Language = user.GetProperty(LanguageProperty, "en")!,
            Permissions = [.. permissions],
        };
    }
}
