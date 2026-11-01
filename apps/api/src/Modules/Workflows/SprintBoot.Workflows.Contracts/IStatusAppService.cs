using Volo.Abp.Application.Services;

namespace SprintBoot.Workflows;

public interface IStatusAppService : IApplicationService
{
    Task<List<StatusDto>> GetListAsync();

    Task<List<StatusAdminDto>> GetAdminListAsync();

    Task<StatusDto> CreateAsync(SaveStatusInput input);

    Task<StatusDto> UpdateAsync(Guid id, SaveStatusInput input);
}
