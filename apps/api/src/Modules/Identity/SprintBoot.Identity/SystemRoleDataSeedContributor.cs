using Microsoft.AspNetCore.Identity;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;

using IdentityRole = Volo.Abp.Identity.IdentityRole;

namespace SprintBoot.Identity;

public class SystemRoleDataSeedContributor(IdentityRoleManager roleManager, IGuidGenerator guids)
    : IDataSeedContributor, ITransientDependency
{
    public async Task SeedAsync(DataSeedContext context)
    {
        if (await roleManager.FindByNameAsync(SystemRoles.Member) is null)
        {
            var role = new IdentityRole(guids.Create(), SystemRoles.Member, context.TenantId) { IsDefault = true, IsPublic = true };
            (await roleManager.CreateAsync(role)).CheckErrors();
        }
    }
}
