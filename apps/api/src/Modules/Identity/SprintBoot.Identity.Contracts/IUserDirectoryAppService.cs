using Volo.Abp.Application.Services;

namespace SprintBoot.Identity;

public class UserRefDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }
}

/// <summary>Minimal user info any signed-in user may see, for pickers and display names.</summary>
public interface IUserDirectoryAppService : IApplicationService
{
    Task<List<UserRefDto>> SearchAsync(string? search);
}

public interface IUserDirectory
{
    Task<Dictionary<Guid, UserRefDto>> GetByIdsAsync(IEnumerable<Guid> ids);
}
