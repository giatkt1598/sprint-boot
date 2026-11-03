using Volo.Abp.Application.Services;

namespace SprintBoot.Identity;

public class UserRefDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }

    /// <summary>Null until the user uploads a picture; clients then show initials.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>The account was deleted; the name then reads Deactivated User.</summary>
    public bool IsDeactivated { get; set; }
}

/// <summary>Minimal user info any signed-in user may see, for pickers and display names.</summary>
public interface IUserDirectoryAppService : IApplicationService
{
    Task<List<UserRefDto>> GetSearchAsync(string? search);
}

public interface IUserDirectory
{
    Task<Dictionary<Guid, UserRefDto>> GetByIdsAsync(IEnumerable<Guid> ids);

    /// <summary>Those of the given users who can still sign in.</summary>
    Task<HashSet<Guid>> GetActiveIdsAsync(IEnumerable<Guid> ids);
}
