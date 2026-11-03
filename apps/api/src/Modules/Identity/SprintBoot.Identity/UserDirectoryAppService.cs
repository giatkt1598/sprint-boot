using Volo.Abp;
using Volo.Abp.Data;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace SprintBoot.Identity;

[Authorize]
public class UserDirectoryAppService(IIdentityUserRepository users) : ApplicationService, IUserDirectoryAppService
{
    private const int MaxResults = 20;

    public async Task<List<UserRefDto>> GetSearchAsync(string? search)
    {
        var page = await users.GetListAsync(nameof(IdentityUser.Name), MaxResults, 0, search);
        return [.. page.Select(UserDirectory.ToDto)];
    }
}

/// <summary>In-process lookups with no calling user required, so background consumers can use them.</summary>
public class UserDirectory(IIdentityUserRepository users, IDataFilter dataFilter) : IUserDirectory, ITransientDependency
{
    public const string DeactivatedName = "Deactivated User";

    /// <summary>Deleted people are included, so their past work still shows who did it.</summary>
    public async Task<Dictionary<Guid, UserRefDto>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            var found = await users.GetListByIdsAsync(ids.Distinct().ToArray());
            return found.ToDictionary(user => user.Id, ToDto);
        }
    }

    public async Task<HashSet<Guid>> GetActiveIdsAsync(IEnumerable<Guid> ids)
    {
        var found = await users.GetListByIdsAsync(ids.Distinct().ToArray());
        var now = DateTimeOffset.UtcNow;
        return [.. found.Where(user => user.IsActive && (user.LockoutEnd is null || user.LockoutEnd <= now)).Select(user => user.Id)];
    }

    public static UserRefDto ToDto(IdentityUser user) => user.IsDeleted
        ? new UserRefDto { Id = user.Id, Name = DeactivatedName, Email = string.Empty, IsDeactivated = true }
        : new()
    {
        Id = user.Id,
        Name = string.IsNullOrWhiteSpace(user.Name) ? user.UserName : user.Name,
        Email = user.Email,
        AvatarUrl = UserAvatars.Url(user),
    };
}
