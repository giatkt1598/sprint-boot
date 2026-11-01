using Microsoft.Extensions.Configuration;
using OpenIddict.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SprintBoot.Identity;

public class SpaClientDataSeedContributor(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IConfiguration configuration) : IDataSeedContributor, ITransientDependency
{
    public const string ClientId = "sprint-boot-web";
    public const string ApiScope = "sprint-boot-api";

    public async Task SeedAsync(DataSeedContext context)
    {
        if (await scopeManager.FindByNameAsync(ApiScope) is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = ApiScope,
                DisplayName = "Sprint Boot API",
                Resources = { ApiScope },
            });
        }

        var baseUrl = (configuration["Auth:SpaBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = ClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Sprint Boot Web",
            RedirectUris = { new Uri($"{baseUrl}/auth/callback") },
            PostLogoutRedirectUris = { new Uri($"{baseUrl}/login") },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + ApiScope,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        var existing = await applicationManager.FindByClientIdAsync(ClientId);
        if (existing is null)
        {
            await applicationManager.CreateAsync(descriptor);
        }
        else
        {
            await applicationManager.UpdateAsync(existing, descriptor);
        }
    }
}
