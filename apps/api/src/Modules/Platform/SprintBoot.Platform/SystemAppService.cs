using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp.Timing;

namespace SprintBoot.Platform;

[AllowAnonymous]
public class SystemAppService(IClock clock) : ApplicationService, ISystemAppService
{
    public Task<SystemPingDto> GetPingAsync()
    {
        var version = typeof(SystemAppService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        return Task.FromResult(new SystemPingDto
        {
            Status = "ok",
            Version = version,
            ServerTime = clock.Now,
        });
    }
}
