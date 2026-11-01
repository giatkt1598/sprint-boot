using Volo.Abp.Application.Services;

namespace SprintBoot.Identity;

public interface IAuthAppService : IApplicationService
{
    /// <summary>Starts a cookie session used only by the authorize endpoint.</summary>
    Task LoginAsync(LoginInput input);

    Task<CurrentUserDto> GetMeAsync();
}
