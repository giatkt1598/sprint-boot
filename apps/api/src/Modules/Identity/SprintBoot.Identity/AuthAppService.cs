using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Identity;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace SprintBoot.Identity;

public class AuthAppService(AbpSignInManager signInManager, IdentityUserManager userManager)
    : ApplicationService, IAuthAppService
{
    /// <summary>Same error for every failure so responses never reveal whether an email exists.</summary>
    public const string InvalidCredentialsCode = "SprintBoot:InvalidCredentials";
    public const string LockedOutCode = "SprintBoot:LockedOut";

    [AllowAnonymous]
    public async Task LoginAsync(LoginInput input)
    {
        var user = await userManager.FindByEmailAsync(input.Email)
            ?? throw new UserFriendlyException("Invalid email or password", InvalidCredentialsCode);

        var result = await signInManager.PasswordSignInAsync(user, input.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            throw new UserFriendlyException("Account is locked", LockedOutCode);
        }

        if (!result.Succeeded)
        {
            throw new UserFriendlyException("Invalid email or password", InvalidCredentialsCode);
        }
    }

    [Authorize]
    public async Task<CurrentUserDto> GetMeAsync()
    {
        var user = await userManager.GetByIdAsync(CurrentUser.GetId());
        return new CurrentUserDto
        {
            Id = user.Id,
            Email = user.Email,
            Name = string.IsNullOrWhiteSpace(user.Name) ? user.UserName : user.Name,
            Roles = [.. await userManager.GetRolesAsync(user)],
        };
    }
}
