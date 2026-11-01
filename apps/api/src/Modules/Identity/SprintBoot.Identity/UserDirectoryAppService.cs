using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace SprintBoot.Identity;

[Authorize]
[Volo.Abp.DependencyInjection.ExposeServices(typeof(IUserDirectoryAppService), typeof(IUserDirectory))]
public class UserDirectoryAppService(IIdentityUserRepository users) : ApplicationService, IUserDirectoryAppService, IUserDirectory
{
    private const int MaxResults = 20;

    public async Task<List<UserRefDto>> GetSearchAsync(string? search)
    {
        var page = await users.GetListAsync(nameof(IdentityUser.Name), MaxResults, 0, search);
        return [.. page.Select(ToDto)];
    }

    [Microsoft.AspNetCore.Mvc.NonAction]
    public async Task<Dictionary<Guid, UserRefDto>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        var found = await users.GetListByIdsAsync(ids.Distinct().ToArray());
        return found.ToDictionary(user => user.Id, ToDto);
    }

    private static UserRefDto ToDto(IdentityUser user) => new()
    {
        Id = user.Id,
        Name = string.IsNullOrWhiteSpace(user.Name) ? user.UserName : user.Name,
        Email = user.Email,
    };
}
