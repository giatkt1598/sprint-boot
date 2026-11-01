using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SprintBoot.Identity;

public interface IUserAdminAppService : IApplicationService
{
    Task<PagedResultDto<UserSummaryDto>> GetListAsync(UserListInput input);

    Task<UserSummaryDto> GetAsync(Guid id);

    Task<UserSummaryDto> CreateAsync(CreateUserInput input);

    Task<UserSummaryDto> LockAsync(Guid id);

    Task<UserSummaryDto> UnlockAsync(Guid id);

    Task<UserSummaryDto> SetSystemRoleAsync(Guid id, SetSystemRoleInput input);
}
