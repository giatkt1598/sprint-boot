using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SprintBoot.Identity;

public interface IUserAdminAppService : IApplicationService
{
    Task<PagedResultDto<UserSummaryDto>> GetListAsync(UserListInput input);

    Task<UserSummaryDto> GetAsync(Guid id);

    Task<List<SignInMethodDto>> GetSignInMethodsAsync(Guid id);

    Task<UserSummaryDto> CreateAsync(CreateUserInput input);

    Task<UserSummaryDto> LockAsync(Guid id);

    Task<UserSummaryDto> UnlockAsync(Guid id);

    Task<UserSummaryDto> SetSystemRoleAsync(Guid id, SetSystemRoleInput input);

    /// <summary>Turns two-factor off for someone who lost their authenticator app; they set it up again at their next sign-in if required.</summary>
    Task<UserSummaryDto> ResetTwoFactorAsync(Guid id);

    /// <summary>
    /// Removes the account: it can no longer sign in and leaves every space. Its work stays,
    /// shown as by a deactivated user. Not for yourself, nor for the last system admin.
    /// </summary>
    Task DeleteAsync(Guid id);
}
