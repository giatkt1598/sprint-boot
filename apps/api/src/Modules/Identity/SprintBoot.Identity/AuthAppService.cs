using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Data;
using SprintBoot.Projects;
using Volo.Abp.Domain.Repositories;
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
    IDistributedEventBus eventBus,
    IRepository<AuthPolicy, Guid> policies,
    IRepository<ExternalProviderConfig, Guid> providers,
    InvitationAcceptor invitationAcceptor,
    IRepository<InvitationProject, Guid> invitationProjects,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    AvatarStore avatars)
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

        var policy = await policies.FirstOrDefaultAsync();
        if (policy is { LocalLoginEnabled: false } && !await userManager.IsInRoleAsync(user, SystemRoles.Admin))
        {
            // Admins keep local sign-in so an IdP outage cannot lock everyone out.
            return LoginOutcome.LocalDisabled;
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
    public async Task<SignInMethodsDto> GetMethodsAsync()
    {
        var policy = await policies.FirstOrDefaultAsync();
        var enabled = await providers.GetListAsync(item => item.IsEnabled);
        return new SignInMethodsDto
        {
            LocalLoginEnabled = policy?.LocalLoginEnabled ?? true,
            Providers = [.. enabled.OrderBy(item => item.DisplayName).Select(item => new SignInProviderDto { Name = item.Name, DisplayName = item.DisplayName })],
        };
    }

    [AllowAnonymous]
    public async Task<InvitationPreviewDto> GetInvitationAsync(TokenInput input)
    {
        var invitation = await invitationAcceptor.GetUsableAsync(input.Token);
        var methods = await GetMethodsAsync();
        var projects = await invitationProjects.GetListAsync(item => item.InvitationId == invitation.Id);
        var names = await projectAccess.GetRefsAsync(projects.Select(item => item.ProjectId));
        var people = await userDirectory.GetByIdsAsync([invitation.InvitedById]);
        return new InvitationPreviewDto
        {
            Email = invitation.Email,
            InvitedBy = people.GetValueOrDefault(invitation.InvitedById)?.Name,
            SystemRole = invitation.SystemRole,
            Projects = [.. projects.Select(item => new InvitationProjectDto
            {
                ProjectId = item.ProjectId,
                ProjectName = names.GetValueOrDefault(item.ProjectId)?.Name ?? string.Empty,
                Role = item.ProjectRole,
            })],
            LocalLoginEnabled = methods.LocalLoginEnabled,
            Providers = methods.Providers,
        };
    }

    [AllowAnonymous]
    public async Task AcceptInvitationAsync(AcceptInvitationInput input)
    {
        var invitation = await invitationAcceptor.GetUsableAsync(input.Token);
        var policy = await policies.FirstOrDefaultAsync();
        if (policy is { LocalLoginEnabled: false })
        {
            throw new UserFriendlyException("Sign in with single sign-on", AuthErrors.LocalDisabled);
        }

        if (await userManager.FindByEmailAsync(invitation.Email) is not null)
        {
            throw new BusinessException(InvitationErrors.Invalid);
        }

        var user = new IdentityUser(GuidGenerator.Create(), invitation.Email, invitation.Email) { Name = input.Name };
        ThrowIfRejected(await userManager.CreateAsync(user, input.Password));
        await invitationAcceptor.ApplyAsync(invitation, user);
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

    public async Task<CurrentUserDto> UploadAvatarAsync(IRemoteStreamContent file)
    {
        var user = await userManager.GetByIdAsync(CurrentUser.GetId());
        await avatars.SaveAsync(user, file);
        return await ToDtoAsync(user);
    }

    public async Task<CurrentUserDto> DeleteAvatarAsync()
    {
        var user = await userManager.GetByIdAsync(CurrentUser.GetId());
        await avatars.RemoveAsync(user);
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
        foreach (var permission in new[] { SprintBootPermissions.ManageUsers, SprintBootPermissions.RestoreIssues, SprintBootPermissions.ManageWorkflows, SprintBootPermissions.ManageNotificationSchemes, SprintBootPermissions.ManageSso, SprintBootPermissions.ManageEmail, SprintBootPermissions.ManageInvitations, SprintBootPermissions.ManageGit })
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
            AvatarUrl = UserAvatars.Url(user),
        };
    }
}
