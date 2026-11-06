using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sustainsys.Saml2;
using Sustainsys.Saml2.AspNetCore2;
using Sustainsys.Saml2.Configuration;
using Sustainsys.Saml2.Metadata;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Uow;

namespace SprintBoot.Identity;

/// <summary>
/// Registers IdP schemes while the app runs. Every use compares the stored version with
/// what this process has, so a change made on one replica reaches the others without a restart.
/// </summary>
public class ExternalSchemeRegistry(
    IAuthenticationSchemeProvider schemes,
    IOptionsMonitorCache<OpenIdConnectOptions> oidcOptions,
    IOptionsMonitorCache<OAuthOptions> oauthOptions,
    IOptionsMonitorCache<Saml2Options> samlOptions,
    IOptions<AuthenticationOptions> authenticationOptions,
    ILoggerFactory loggerFactory,
    IHttpClientFactory httpClients,
    IDataProtectionProvider dataProtection,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : ISingletonDependency
{
    public const string CallbackPrefix = "/connect/signin-";
    public const string GoogleDiscovery = "https://accounts.google.com";

    private readonly ConcurrentDictionary<string, string> _versions = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static string SchemeName(string provider) => $"external-{provider}";

    /// <summary>The provider a callback path belongs to: SAML adds its own segments, such as /Acs.</summary>
    public static string ProviderOfCallback(string path)
    {
        var rest = path[CallbackPrefix.Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }

    /// <summary>LDAP signs in on our own page; it has no scheme to register.</summary>
    public static bool Redirects(ExternalProviderType type) => type != ExternalProviderType.Ldap;

    /// <summary>What to give the IdP: it serves this service's metadata, and /Acs below it takes the answer.</summary>
    public static string SamlMetadataPath(string provider) => CallbackPrefix + provider;

    /// <summary>Returns false when no enabled provider has this name.</summary>
    public async Task<bool> EnsureAsync(string provider)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        using var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var config = await services.GetRequiredService<IRepository<ExternalProviderConfig, Guid>>()
            .FirstOrDefaultAsync(item => item.Name == provider);
        var name = SchemeName(provider);

        await _gate.WaitAsync();
        try
        {
            if (config is not { IsEnabled: true } || !Redirects(config.Type))
            {
                Remove(name);
                return false;
            }

            if (_versions.TryGetValue(name, out var version) && version == config.ConcurrencyStamp)
            {
                return true;
            }

            Remove(name);
            if (config.Type == ExternalProviderType.Saml2)
            {
                await RegisterSamlAsync(name, config);
                _versions[name] = config.ConcurrencyStamp;
                return true;
            }

            var secret = config.EncryptedSecret is null
                ? null
                : services.GetRequiredService<IStringEncryptionService>().Decrypt(config.EncryptedSecret);
            Register(name, config, secret);
            _versions[name] = config.ConcurrencyStamp;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Admins paste either the issuer or the full discovery document address.</summary>
    public static string? ToAuthority(string? discoveryUrl) =>
        discoveryUrl?.Trim().Replace("/.well-known/openid-configuration", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('/');

    private void Remove(string name)
    {
        schemes.RemoveScheme(name);
        oidcOptions.TryRemove(name);
        oauthOptions.TryRemove(name);
        samlOptions.TryRemove(name);
        _versions.TryRemove(name, out _);
    }

    private void Register(string name, ExternalProviderConfig config, string? secret)
    {
        if (config.Type == ExternalProviderType.GitHub)
        {
            var options = GitHubOptions(config, secret);
            new OAuthPostConfigureOptions<OAuthOptions, OAuthHandler<OAuthOptions>>(dataProtection).PostConfigure(name, options);
            oauthOptions.TryAdd(name, options);
            schemes.AddScheme(new AuthenticationScheme(name, config.DisplayName, typeof(OAuthHandler<OAuthOptions>)));
            return;
        }

        var oidc = new OpenIdConnectOptions
        {
            SignInScheme = IdentityConstants.ExternalScheme,
            Authority = config.Type == ExternalProviderType.Google ? GoogleDiscovery : ToAuthority(config.DiscoveryUrl),
            ClientId = config.ClientId,
            ClientSecret = secret,
            ResponseType = "code",
            // The default form_post comes back as a cross-site POST when the IdP lives on another
            // domain, and browsers drop the Lax correlation cookie on those: "Correlation failed".
            // A redirect with the code in the query is a top-level GET, which keeps the cookie.
            ResponseMode = OpenIdConnectResponseMode.Query,
            UsePkce = true,
            CallbackPath = CallbackPrefix + config.Name,
            GetClaimsFromUserInfoEndpoint = true,
            MapInboundClaims = false,
            RequireHttpsMetadata = configuration.GetValue("Auth:RequireHttps", defaultValue: true),
        };
        // The user info endpoint is where some providers put the picture; keep it next to the name.
        oidc.ClaimActions.MapUniqueJsonKey("picture", "picture");
        oidc.Scope.Clear();
        foreach (var scope in (config.Scopes ?? "openid profile email").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            oidc.Scope.Add(scope);
        }

        // Lax survives the redirect back from the IdP, also over plain HTTP in development.
        oidc.CorrelationCookie.SameSite = SameSiteMode.Lax;
        oidc.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        oidc.NonceCookie.SameSite = SameSiteMode.Lax;
        oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        new OpenIdConnectPostConfigureOptions(dataProtection).PostConfigure(name, oidc);
        oidcOptions.TryAdd(name, oidc);
        schemes.AddScheme(new AuthenticationScheme(name, config.DisplayName, typeof(OpenIdConnectHandler)));
    }

    /// <summary>
    /// The IdP's entity ID comes from its metadata, read here first: Sustainsys needs it before it loads the rest.
    /// Paths sit under the callback prefix, so the session cookie and the replica warm-up both apply.
    /// </summary>
    private async Task RegisterSamlAsync(string name, ExternalProviderConfig config)
    {
        using var client = httpClients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var idpEntityId = await SamlMetadata.ReadEntityIdAsync(client, config.DiscoveryUrl!);

        var sp = new SPOptions
        {
            EntityId = new EntityId(config.ClientId),
            ModulePath = SamlMetadataPath(config.Name),
            ReturnUrl = new Uri("/connect/external/complete", UriKind.Relative),
            // We hold no SAML signing certificate, so requests go unsigned even when the IdP metadata asks
            // for signed ones; IdPs such as Keycloak then check that per client instead.
            AuthenticateRequestSigningBehavior = SigningBehavior.Never,
        };
        if (configuration["Auth:SpaBaseUrl"] is { Length: > 0 } publicUrl)
        {
            // Behind a proxy the request says http://internal; the IdP must post back to the public address.
            sp.PublicOrigin = new Uri(publicUrl.TrimEnd('/') + "/");
        }

        var options = new Saml2Options { SignInScheme = IdentityConstants.ExternalScheme, SPOptions = sp };
        options.IdentityProviders.Add(new IdentityProvider(new EntityId(idpEntityId), sp)
        {
            MetadataLocation = config.DiscoveryUrl,
            LoadMetadata = true,
            // Only answers to a sign-in we started carry our state, invitation and return address.
            AllowUnsolicitedAuthnResponse = false,
        });
        new PostConfigureSaml2Options(loggerFactory, authenticationOptions).PostConfigure(name, options);
        samlOptions.TryAdd(name, options);
        schemes.AddScheme(new AuthenticationScheme(name, config.DisplayName, typeof(Saml2Handler)));
    }

    private static OAuthOptions GitHubOptions(ExternalProviderConfig config, string? secret)
    {
        var options = new OAuthOptions
        {
            SignInScheme = IdentityConstants.ExternalScheme,
            ClientId = config.ClientId,
            ClientSecret = secret ?? string.Empty,
            CallbackPath = CallbackPrefix + config.Name,
            AuthorizationEndpoint = "https://github.com/login/oauth/authorize",
            TokenEndpoint = "https://github.com/login/oauth/access_token",
            UserInformationEndpoint = "https://api.github.com/user",
            UsePkce = true,
        };
        options.Scope.Add("read:user");
        options.Scope.Add("user:email");
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnCreatingTicket = LoadGitHubIdentityAsync;
        return options;
    }

    private static async Task LoadGitHubIdentityAsync(OAuthCreatingTicketContext context)
    {
        async Task<JsonDocument> GetAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("SprintBoot");
            using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
            response.EnsureSuccessStatusCode();
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted));
        }

        using var user = await GetAsync(context.Options.UserInformationEndpoint);
        using var emails = await GetAsync("https://api.github.com/user/emails");
        var (email, verified) = GitHubEmails.Pick(emails.RootElement);

        context.Identity!.AddClaim(new Claim("sub", user.RootElement.GetProperty("id").GetRawText()));
        context.Identity.AddClaim(new Claim("name", user.RootElement.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty));
        if (email is not null)
        {
            context.Identity.AddClaim(new Claim("email", email));
            context.Identity.AddClaim(new Claim("email_verified", verified ? "true" : "false"));
        }
    }
}

public static class GitHubEmails
{
    /// <summary>GitHub's public profile email is unverified, so only this list can be trusted.</summary>
    public static (string? Email, bool Verified) Pick(JsonElement emails)
    {
        string? fallback = null;
        foreach (var item in emails.EnumerateArray())
        {
            var address = item.GetProperty("email").GetString();
            var verified = item.GetProperty("verified").GetBoolean();
            if (item.GetProperty("primary").GetBoolean())
            {
                return (address, verified);
            }

            fallback ??= verified ? address : null;
        }

        return (fallback, fallback is not null);
    }
}
