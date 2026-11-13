using System.Security.Claims;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;

namespace SprintBoot.Identity;

public class UserImpersonation(IdentityUserManager users) : IUserImpersonation, ITransientDependency
{
    public async Task<ClaimsPrincipal?> FindPrincipalAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user))
        {
            return null;
        }

        // The same claims a personal token carries, so system permissions resolve as for the person.
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, user.Id.ToString()),
            new(AbpClaimTypes.UserName, user.UserName),
            new(AbpClaimTypes.Email, user.Email),
        };
        claims.AddRange((await users.GetRolesAsync(user)).Select(role => new Claim(AbpClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Integration"));
    }
}
