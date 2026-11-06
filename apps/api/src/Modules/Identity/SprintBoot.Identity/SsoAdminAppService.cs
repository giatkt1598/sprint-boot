using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Encryption;

namespace SprintBoot.Identity;

[Authorize(SprintBootPermissions.ManageSso)]
public class SsoAdminAppService(
    IRepository<ExternalProviderConfig, Guid> providers,
    IRepository<AuthPolicy, Guid> policies,
    IStringEncryptionService encryption,
    IHttpClientFactory httpClients,
    ProviderLogos logos,
    LdapAuthenticator ldap)
    : ApplicationService, ISsoAdminAppService
{
    public async Task<List<ExternalProviderDto>> GetProvidersAsync()
    {
        var list = await providers.GetListAsync();
        return [.. list.OrderBy(item => item.DisplayName).Select(ToDto)];
    }

    public async Task<ExternalProviderDto> CreateProviderAsync(SaveProviderInput input)
    {
        if (await providers.AnyAsync(item => item.Name == input.Name))
        {
            throw new BusinessException(SsoErrors.ProviderNameTaken);
        }

        var provider = new ExternalProviderConfig(GuidGenerator.Create(), input.Name);
        Apply(provider, input);
        await providers.InsertAsync(provider, autoSave: true);
        return ToDto(provider);
    }

    public async Task<ExternalProviderDto> UpdateProviderAsync(Guid id, SaveProviderInput input)
    {
        var provider = await providers.GetAsync(id);
        Apply(provider, input);
        await EnsureAMethodRemainsAsync(null, provider.Id, input.IsEnabled);
        await providers.UpdateAsync(provider, autoSave: true);
        return ToDto(provider);
    }

    public async Task DeleteProviderAsync(Guid id)
    {
        await EnsureAMethodRemainsAsync(null, id, enabled: false);
        await providers.DeleteAsync(id);
    }

    public async Task<ExternalProviderDto> UploadLogoAsync(Guid id, IRemoteStreamContent file)
    {
        var provider = await providers.GetAsync(id);
        await logos.SaveAsync(provider, file);
        await providers.UpdateAsync(provider, autoSave: true);
        return ToDto(provider);
    }

    public async Task<ExternalProviderDto> DeleteLogoAsync(Guid id)
    {
        var provider = await providers.GetAsync(id);
        await logos.RemoveAsync(provider);
        await providers.UpdateAsync(provider, autoSave: true);
        return ToDto(provider);
    }

    public async Task<ConnectionTestDto> TestConnectionAsync(TestConnectionInput input)
    {
        if (input.Type == ExternalProviderType.Ldap)
        {
            var password = input.ClientSecret;
            if (string.IsNullOrEmpty(password) && input.ProviderId is { } providerId
                && (await providers.FindAsync(providerId))?.EncryptedSecret is { } stored)
            {
                password = encryption.Decrypt(stored);
            }

            var problem = await ldap.TestAsync(new LdapServer(input.DiscoveryUrl.Trim(), input.ClientId ?? string.Empty, password));
            return new ConnectionTestDto { Succeeded = problem is null, Issuer = problem is null ? input.DiscoveryUrl.Trim() : null };
        }

        if (input.Type == ExternalProviderType.Saml2)
        {
            try
            {
                using var metadata = httpClients.CreateClient();
                metadata.Timeout = TimeSpan.FromSeconds(10);
                return new ConnectionTestDto { Succeeded = true, Issuer = await SamlMetadata.ReadEntityIdAsync(metadata, input.DiscoveryUrl.Trim()) };
            }
            catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or System.Xml.XmlException or UriFormatException or InvalidOperationException)
            {
                Logger.LogInformation(failure, "SAML metadata test failed for {Url}", input.DiscoveryUrl);
                return new ConnectionTestDto { Succeeded = false, Issuer = null };
            }
        }

        var url = $"{ExternalSchemeRegistry.ToAuthority(input.DiscoveryUrl)}/.well-known/openid-configuration";
        try
        {
            using var client = httpClients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var document = JsonDocument.Parse(await client.GetStringAsync(url));
            var issuer = document.RootElement.GetProperty("issuer").GetString();
            return new ConnectionTestDto { Succeeded = issuer is not null, Issuer = issuer };
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or UriFormatException or InvalidOperationException)
        {
            Logger.LogInformation(failure, "Discovery test failed for {Url}", url);
            return new ConnectionTestDto { Succeeded = false, Issuer = null };
        }
    }

    public async Task<AuthPolicyDto> GetPolicyAsync() => ToDto(await GetOrCreatePolicyAsync());

    public async Task<AuthPolicyDto> UpdatePolicyAsync(SaveAuthPolicyInput input)
    {
        await EnsureAMethodRemainsAsync(input.LocalLoginEnabled, null, false);
        var policy = await GetOrCreatePolicyAsync();
        policy.LocalLoginEnabled = input.LocalLoginEnabled;
        policy.SelfRegistrationEnabled = input.SelfRegistrationEnabled;
        policy.AllowedDomains = input.AllowedDomains;
        await policies.UpdateAsync(policy);
        return ToDto(policy);
    }

    /// <summary>Stops an admin from switching off the last way to sign in.</summary>
    private async Task EnsureAMethodRemainsAsync(bool? localEnabled, Guid? changedProvider, bool enabled)
    {
        var local = localEnabled ?? (await GetOrCreatePolicyAsync()).LocalLoginEnabled;
        var others = await providers.AnyAsync(item => item.IsEnabled && item.Id != changedProvider);
        if (!local && !others && !(changedProvider.HasValue && enabled))
        {
            throw new BusinessException(SsoErrors.LastMethod);
        }
    }

    private async Task<AuthPolicy> GetOrCreatePolicyAsync() =>
        await policies.FirstOrDefaultAsync()
        ?? await policies.InsertAsync(new AuthPolicy(GuidGenerator.Create()), autoSave: true);

    private void Apply(ExternalProviderConfig provider, SaveProviderInput input)
    {
        // OIDC needs its issuer, SAML its metadata and LDAP its server: all of them live in DiscoveryUrl.
        if (input.Type is ExternalProviderType.Oidc or ExternalProviderType.Saml2 or ExternalProviderType.Ldap
            && string.IsNullOrWhiteSpace(input.DiscoveryUrl))
        {
            throw new BusinessException(SsoErrors.DiscoveryRequired);
        }

        if (input.Type == ExternalProviderType.Ldap)
        {
            if (!Uri.TryCreate(input.DiscoveryUrl!.Trim(), UriKind.Absolute, out var server) || server.Scheme is not ("ldap" or "ldaps"))
            {
                throw new BusinessException(SsoErrors.LdapAddressInvalid);
            }

            if (string.IsNullOrWhiteSpace(input.SearchBase))
            {
                throw new BusinessException(SsoErrors.LdapSearchBaseRequired);
            }

            if (!string.IsNullOrWhiteSpace(input.UserFilter) && !input.UserFilter.Contains("{0}", StringComparison.Ordinal))
            {
                throw new BusinessException(SsoErrors.LdapFilterInvalid);
            }
        }

        LdapSettings.Set(provider, input.Type == ExternalProviderType.Ldap ? input.SearchBase : null, input.Type == ExternalProviderType.Ldap ? input.UserFilter : null);

        provider.DisplayName = input.DisplayName;
        provider.Type = input.Type;
        ProviderLogos.SetColor(provider, input.ButtonColor);
        provider.DiscoveryUrl = input.DiscoveryUrl;
        provider.ClientId = input.ClientId;
        provider.Scopes = input.Scopes;
        provider.EmailClaim = input.EmailClaim;
        provider.NameClaim = input.NameClaim;
        provider.IsEnabled = input.IsEnabled;
        if (!string.IsNullOrEmpty(input.ClientSecret))
        {
            provider.EncryptedSecret = encryption.Encrypt(input.ClientSecret);
        }
    }

    private static ExternalProviderDto ToDto(ExternalProviderConfig provider) => new()
    {
        Id = provider.Id,
        Name = provider.Name,
        DisplayName = provider.DisplayName,
        Type = provider.Type,
        DiscoveryUrl = provider.DiscoveryUrl,
        ClientId = provider.ClientId,
        HasSecret = provider.EncryptedSecret is not null,
        Scopes = provider.Scopes,
        EmailClaim = provider.EmailClaim,
        NameClaim = provider.NameClaim,
        IsEnabled = provider.IsEnabled,
        LogoUrl = ProviderLogos.Url(provider),
        ButtonColor = ProviderLogos.Color(provider),
        SearchBase = LdapSettings.SearchBase(provider),
        UserFilter = provider.Type == ExternalProviderType.Ldap ? LdapSettings.UserFilter(provider) : null,
    };

    private static AuthPolicyDto ToDto(AuthPolicy policy) => new()
    {
        LocalLoginEnabled = policy.LocalLoginEnabled,
        SelfRegistrationEnabled = policy.SelfRegistrationEnabled,
        AllowedDomains = policy.AllowedDomains,
    };
}
