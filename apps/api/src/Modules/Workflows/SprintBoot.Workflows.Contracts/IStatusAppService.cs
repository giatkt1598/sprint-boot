using Volo.Abp.Application.Services;

namespace SprintBoot.Workflows;

public interface IStatusAppService : IApplicationService
{
    /// <summary>Global statuses, plus the space's own when a space is given.</summary>
    Task<List<StatusDto>> GetListAsync(Guid? projectId);

    Task<List<StatusAdminDto>> GetAdminListAsync(StatusListInput input);

    Task<StatusDto> CreateAsync(SaveStatusInput input);

    Task<StatusDto> UpdateAsync(Guid id, SaveStatusInput input);
}
