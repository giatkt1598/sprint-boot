using Volo.Abp.Application.Services;

namespace SprintBoot.Platform;

public interface ISystemAppService : IApplicationService
{
    Task<SystemPingDto> GetPingAsync();
}
