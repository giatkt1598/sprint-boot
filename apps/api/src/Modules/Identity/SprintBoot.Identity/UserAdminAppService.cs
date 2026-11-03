using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using OpenIddict.Abstractions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace SprintBoot.Identity;

[Authorize(SprintBootPermissions.ManageUsers)]
public class UserAdminAppService(
    IdentityUserManager userManager,
    IIdentityUserRepository users,
    IRepository<PersonalToken, Guid> personalTokens,
    IOpenIddictTokenManager oidcTokens,
    IDistributedEventBus eventBus)
    : ApplicationService, IUserAdminAppService
{
    /// <summary>Stable value the UI translates; external logins show their provider name.</summary>
    public const string PasswordMethod = "password";

    private static readonly DateTimeOffset LockedForever = DateTimeOffset.MaxValue;

    public async Task<PagedResultDto<UserSummaryDto>> GetListAsync(UserListInput input)
    {
        var total = await users.GetCountAsync(input.Search);
        var page = await users.GetListAsync(
            nameof(IdentityUser.Name), input.MaxResultCount, input.SkipCount, input.Search);

        var items = new List<UserSummaryDto>();
        foreach (var user in page)
        {
            items.Add(await ToDtoAsync(user));
        }

        return new PagedResultDto<UserSummaryDto>(total, items);
    }

    public async Task<UserSummaryDto> GetAsync(Guid id) => await ToDtoAsync(await userManager.GetByIdAsync(id));

    public async Task<List<SignInMethodDto>> GetSignInMethodsAsync(Guid id)
    {
        var user = await userManager.GetByIdAsync(id);
        var methods = new List<SignInMethodDto>();

        if (await userManager.HasPasswordAsync(user))
        {
            methods.Add(new SignInMethodDto
            {
                Method = PasswordMethod,
                IsEnabled = true,
                Account = user.Email,
                LinkedAt = user.CreationTime,
                LastUsedAt = user.LastSignInTime?.UtcDateTime,
            });
        }

        foreach (var login in await userManager.GetLoginsAsync(user))
        {
            methods.Add(new SignInMethodDto
            {
                Method = login.ProviderDisplayName ?? login.LoginProvider,
                IsEnabled = true,
                Account = user.Email,
                LinkedAt = null,
                LastUsedAt = null,
            });
        }

        return methods;
    }

    public async Task<UserSummaryDto> CreateAsync(CreateUserInput input)
    {
        var user = new IdentityUser(GuidGenerator.Create(), input.Email, input.Email, CurrentTenant.Id) { Name = input.Name };
        (await userManager.CreateAsync(user, input.Password)).CheckErrors();
        await EnsureRoleExistsAsync(input.SystemRole);
        (await userManager.AddToRoleAsync(user, input.SystemRole)).CheckErrors();
        return await ToDtoAsync(user);
    }

    public async Task<UserSummaryDto> LockAsync(Guid id)
    {
        if (id == CurrentUser.GetId())
        {
            throw new UserFriendlyException("You cannot lock your own account", AuthErrors.CannotLockSelf);
        }

        var user = await userManager.GetByIdAsync(id);
        (await userManager.SetLockoutEnabledAsync(user, true)).CheckErrors();
        (await userManager.SetLockoutEndDateAsync(user, LockedForever)).CheckErrors();
        return await ToDtoAsync(user);
    }

    public async Task<UserSummaryDto> UnlockAsync(Guid id)
    {
        var user = await userManager.GetByIdAsync(id);
        (await userManager.SetLockoutEndDateAsync(user, null)).CheckErrors();
        return await ToDtoAsync(user);
    }

    public async Task<UserSummaryDto> SetSystemRoleAsync(Guid id, SetSystemRoleInput input)
    {
        await EnsureRoleExistsAsync(input.SystemRole);
        var user = await userManager.GetByIdAsync(id);
        (await userManager.SetRolesAsync(user, [input.SystemRole])).CheckErrors();
        return await ToDtoAsync(user);
    }

    public async Task DeleteAsync(Guid id)
    {
        if (id == CurrentUser.GetId())
        {
            throw new BusinessException(AuthErrors.CannotDeleteSelf);
        }

        var user = await userManager.GetByIdAsync(id);
        if (await userManager.IsInRoleAsync(user, SystemRoles.Admin)
            && (await userManager.GetUsersInRoleAsync(SystemRoles.Admin)).All(admin => admin.Id == id))
        {
            throw new BusinessException(AuthErrors.LastSystemAdmin);
        }

        // Nothing it signed in with may keep working, and its IdP logins are freed for a new account.
        await personalTokens.DeleteAsync(token => token.UserId == id, autoSave: true);
        await foreach (var token in oidcTokens.FindBySubjectAsync(id.ToString()))
        {
            await oidcTokens.TryRevokeAsync(token);
        }

        foreach (var login in await userManager.GetLoginsAsync(user))
        {
            (await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey)).CheckErrors();
        }

        // Soft delete: issues, comments and history keep pointing at the person.
        (await userManager.DeleteAsync(user)).CheckErrors();
        await eventBus.PublishAsync(new UserDeletedEto { EventId = GuidGenerator.Create(), UserId = id }, useOutbox: true);
    }

    private static Task EnsureRoleExistsAsync(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Member))
        {
            throw new UserFriendlyException($"Unknown system role '{role}'");
        }

        return Task.CompletedTask;
    }

    private async Task<UserSummaryDto> ToDtoAsync(IdentityUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new UserSummaryDto
        {
            Id = user.Id,
            Name = string.IsNullOrWhiteSpace(user.Name) ? user.UserName : user.Name,
            Email = user.Email,
            SystemRole = roles.Contains(SystemRoles.Admin) ? SystemRoles.Admin : SystemRoles.Member,
            IsLocked = await userManager.IsLockedOutAsync(user),
            CreationTime = user.CreationTime,
            LastSignInAt = user.LastSignInTime?.UtcDateTime,
        };
    }
}
