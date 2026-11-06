using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Shouldly;
using SprintBoot.Identity;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>A real OpenLDAP server, with people added through the protocol so no seed files are needed.</summary>
public sealed class LdapServerFixture : IAsyncLifetime
{
    public const string BaseDn = "dc=example,dc=org";
    public const string AdminDn = "cn=admin,dc=example,dc=org";
    public const string AdminPassword = "admin";

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("osixia/openldap:1.5.0")
        .WithPortBinding(389, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("slapd starting"))
        .Build();

    public string Address => $"ldap://localhost:{_container.GetMappedPublicPort(389)}";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        // slapd logs that it starts a moment before it accepts binds.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var connection = Connect();
                connection.Bind(new NetworkCredential(AdminDn, AdminPassword));
                connection.SendRequest(new AddRequest($"ou=people,{BaseDn}", new DirectoryAttribute("objectClass", "organizationalUnit"), new DirectoryAttribute("ou", "people")));
                return;
            }
            catch (LdapException) when (attempt < 30)
            {
                await Task.Delay(500);
            }
        }
    }

    public void AddPerson(string uid, string password, string mail, string displayName)
    {
        using var connection = Connect();
        connection.Bind(new NetworkCredential(AdminDn, AdminPassword));
        connection.SendRequest(new AddRequest(
            $"uid={uid},ou=people,{BaseDn}",
            new DirectoryAttribute("objectClass", "inetOrgPerson"),
            new DirectoryAttribute("uid", uid),
            new DirectoryAttribute("cn", displayName),
            new DirectoryAttribute("sn", displayName),
            new DirectoryAttribute("displayName", displayName),
            new DirectoryAttribute("mail", mail),
            new DirectoryAttribute("userPassword", password)));
    }

    private LdapConnection Connect()
    {
        var uri = new Uri(Address);
        var connection = new LdapConnection(new LdapDirectoryIdentifier(uri.Host, uri.Port)) { AuthType = AuthType.Basic };
        connection.SessionOptions.ProtocolVersion = 3;
        return connection;
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[Collection(ApiCollection.Name)]
public class LdapSignInTests(ApiFixture fixture, LdapServerFixture ldap) : IClassFixture<LdapServerFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<AuthClient> AdminAsync()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        return admin;
    }

    private async Task<string> CreateProviderAsync(AuthClient admin, string? searchBase = null, bool? trustEmail = null)
    {
        var name = $"ldap-{Random.Shared.Next(100000, 999999)}";
        var response = await admin.Http.PostAsJsonAsync("/api/v1/sso-admin/provider", new
        {
            name,
            displayName = "Corporate directory",
            type = ExternalProviderType.Ldap,
            discoveryUrl = ldap.Address,
            clientId = LdapServerFixture.AdminDn,
            clientSecret = LdapServerFixture.AdminPassword,
            searchBase = searchBase ?? $"ou=people,{LdapServerFixture.BaseDn}",
            isEnabled = true,
            trustEmail,
        }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return name;
    }

    private static async Task<HttpResponseMessage> LoginAsync(AuthClient client, string provider, string userName, string password)
    {
        var csrf = await client.GetCsrfTokenAsync(Token);
        return await client.PostJsonAsync("/api/v1/auth/ldap-login", new { provider, userName, password }, csrf, Token);
    }

    [Fact]
    public async Task A_directory_person_signs_in_with_their_password_and_keeps_one_account()
    {
        var admin = await AdminAsync();
        (await admin.Http.PutAsJsonAsync("/api/v1/sso-admin/policy", new { localLoginEnabled = true, selfRegistrationEnabled = true }, Token)).EnsureSuccessStatusCode();
        var provider = await CreateProviderAsync(admin);
        var uid = $"p{Random.Shared.Next(100000, 999999)}";
        var mail = $"{uid}@corp.example";
        ldap.AddPerson(uid, "Directory123!", mail, "Directory Person");

        var methods = await new AuthClient(fixture).Http.GetFromJsonAsync<SignInMethodsDto>("/api/v1/auth/methods", Token);
        methods!.Providers.ShouldContain(item => item.Name == provider && item.Type == ExternalProviderType.Ldap);

        var client = new AuthClient(fixture);
        (await (await LoginAsync(client, provider, uid, "wrong")).Content.ReadAsStringAsync(Token)).ShouldContain(AuthErrors.InvalidCredentials);
        // An empty password would be an anonymous bind, which proves nothing.
        (await LoginAsync(client, provider, uid, "")).IsSuccessStatusCode.ShouldBeFalse();
        (await (await LoginAsync(client, provider, "*", "Directory123!")).Content.ReadAsStringAsync(Token)).ShouldContain(AuthErrors.InvalidCredentials);

        var login = await LoginAsync(client, provider, uid, "Directory123!");
        login.StatusCode.ShouldBe(HttpStatusCode.NoContent, await login.Content.ReadAsStringAsync(Token));
        await client.AuthorizeAsync(Token);
        var me = (await client.Http.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Token))!;
        me.Email.ShouldBe(mail);
        me.Name.ShouldBe("Directory Person");

        // Signing in by email finds the same person, and the same account.
        var again = new AuthClient(fixture);
        (await LoginAsync(again, provider, mail, "Directory123!")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await again.AuthorizeAsync(Token);
        (await again.Http.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Token))!.Id.ShouldBe(me.Id);
    }

    [Fact]
    public async Task An_untrusted_directory_email_never_takes_over_an_existing_account()
    {
        var admin = await AdminAsync();
        var trusted = await CreateProviderAsync(admin);
        var untrusted = await CreateProviderAsync(admin, trustEmail: false);
        var providers = (await admin.Http.GetFromJsonAsync<List<ExternalProviderDto>>("/api/v1/sso-admin/providers", Token))!;
        providers.Single(item => item.Name == trusted).TrustEmail.ShouldBeTrue();
        providers.Single(item => item.Name == untrusted).TrustEmail.ShouldBeFalse();

        // Someone the directory lets claim the admin's address.
        var uid = $"m{Random.Shared.Next(100000, 999999)}";
        ldap.AddPerson(uid, "Directory123!", ApiFixture.AdminEmail, "Mallory");

        var login = await LoginAsync(new AuthClient(fixture), untrusted, uid, "Directory123!");
        (await login.Content.ReadAsStringAsync(Token)).ShouldContain(SsoErrors.EmailNotVerified);
    }

    [Fact]
    public async Task Admins_test_the_bind_and_bad_settings_are_refused()
    {
        var admin = await AdminAsync();

        var ok = await admin.Http.PostAsJsonAsync("/api/v1/sso-admin/test-connection", new
        {
            type = ExternalProviderType.Ldap, discoveryUrl = ldap.Address, clientId = LdapServerFixture.AdminDn, clientSecret = LdapServerFixture.AdminPassword,
        }, Token);
        (await ok.Content.ReadFromJsonAsync<ConnectionTestDto>(Token))!.Succeeded.ShouldBeTrue();
        var wrong = await admin.Http.PostAsJsonAsync("/api/v1/sso-admin/test-connection", new
        {
            type = ExternalProviderType.Ldap, discoveryUrl = ldap.Address, clientId = LdapServerFixture.AdminDn, clientSecret = "nope",
        }, Token);
        (await wrong.Content.ReadFromJsonAsync<ConnectionTestDto>(Token))!.Succeeded.ShouldBeFalse();

        var http = await admin.Http.PostAsJsonAsync("/api/v1/sso-admin/provider", new
        {
            name = $"ldap-{Random.Shared.Next(100000, 999999)}", displayName = "Bad", type = ExternalProviderType.Ldap,
            discoveryUrl = "https://directory", clientId = "cn=x", searchBase = "dc=x", isEnabled = false,
        }, Token);
        (await http.Content.ReadAsStringAsync(Token)).ShouldContain(SsoErrors.LdapAddressInvalid);
        var filter = await admin.Http.PostAsJsonAsync("/api/v1/sso-admin/provider", new
        {
            name = $"ldap-{Random.Shared.Next(100000, 999999)}", displayName = "Bad", type = ExternalProviderType.Ldap,
            discoveryUrl = ldap.Address, clientId = "cn=x", searchBase = "dc=x", userFilter = "(uid=admin)", isEnabled = false,
        }, Token);
        (await filter.Content.ReadAsStringAsync(Token)).ShouldContain(SsoErrors.LdapFilterInvalid);
    }
}
