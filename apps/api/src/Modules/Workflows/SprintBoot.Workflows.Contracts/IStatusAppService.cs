using Volo.Abp.Application.Services;

namespace SprintBoot.Workflows;

public interface IStatusAppService : IApplicationService
{
    Task<List<StatusDto>> GetListAsync();
}
