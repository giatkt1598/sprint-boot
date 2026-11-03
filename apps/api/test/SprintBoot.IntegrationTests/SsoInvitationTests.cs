using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Identity;
using Volo.Abp.Uow;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Account linking security and the acceptance criteria of Invite User.</summary>
[Collection(ApiCollection.Name)]
public class SsoInvitationTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<AuthClient> AdminAsync()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        return admin;
    }

    private async Task<(Guid UserId, string? Error)> LinkAsync(ExternalIdentity identity, string? invitation = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        try
        {
            var user = await scope.ServiceProvider.GetRequiredService<ExternalAccountLinker>().ResolveAsync(identity, invitation);
            await uow.CompleteAsync(Token);
            return (user.Id, null);
        }
        catch (BusinessException failure)
        {
            return (Guid.Empty, failure.Code);
        }
    }

    private static ExternalIdentity Identity(string email, bool verified = true, string? subject = null) =>
        new("keycloak", "Keycloak", subject ?? Guid.NewGuid().ToString(), email, verified, "External Person");

    private async Task SetPolicyAsync(bool selfRegistration, string? domains = null, bool local = true)
    {
        var admin = await AdminAsync();
        (await admin.Http.PutAsJsonAsync("/api/v1/sso-admin/policy", new { localLoginEnabled = local, selfRegistrationEnabled = selfRegistration, allowedDomains = domains }, Token)).EnsureSuccessStatusCode();
    }

    private static string TokenOf(string link) => WebUtility.UrlDecode(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

    private static async Task<InvitationLinkDto> InviteAsync(AuthClient client, object body)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/invitation", body, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<InvitationLinkDto>(Token))!;
    }

    private async Task<HttpResponseMessage> AcceptAsync(string token, string name = "New Person", string password = "Accepted123!x")
    {
        var client = new AuthClient(fixture);
        var csrf = await client.GetCsrfTokenAsync(Token);
        return await client.PostJsonAsync("/api/v1/auth/accept-invitation", new { token, name, password }, csrf, Token);
    }

    private static async Task<ProjectDto> ProjectAsync(AuthClient client)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Inv", key = $"I{Random.Shared.Next(100000, 999999)}", type = 0 }, Token);
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
    }

    private static string NewEmail() => $"invitee-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Unverified_external_email_cannot_take_over_an_account()
    {
        var (user, _) = await fixture.CreateUserAsync(Token);

        var (_, error) = await LinkAsync(Identity(user.Email, verified: false));

        error.ShouldBe(SsoErrors.EmailNotVerified);
    }

    [Fact]
    public async Task Verified_email_links_and_later_sign_ins_follow_the_subject_after_an_email_change()
    {
        var (user, _) = await fixture.CreateUserAsync(Token);
        var subject = Guid.NewGuid().ToString();

        (await LinkAsync(Identity(user.Email, subject: subject))).UserId.ShouldBe(user.Id);
        (await LinkAsync(Identity("renamed@elsewhere.io", subject: subject))).UserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task Self_registration_respects_the_policy_and_domain_list()
    {
        await SetPolicyAsync(selfRegistration: false);
        (await LinkAsync(Identity($"a{Guid.NewGuid():N}@corp.com"))).Error.ShouldBe(SsoErrors.RegistrationClosed);

        await SetPolicyAsync(selfRegistration: true, domains: "corp.com");
        (await LinkAsync(Identity($"b{Guid.NewGuid():N}@other.io"))).Error.ShouldBe(SsoErrors.DomainNotAllowed);
        (await LinkAsync(Identity($"c{Guid.NewGuid():N}@corp.com", verified: false))).Error.ShouldBe(SsoErrors.EmailNotVerified);
        (await LinkAsync(Identity($"d{Guid.NewGuid():N}@corp.com"))).Error.ShouldBeNull();

        await SetPolicyAsync(selfRegistration: false);
    }

    [Fact]
    public async Task The_last_sign_in_method_cannot_be_switched_off()
    {
        var admin = await AdminAsync();

        var response = await admin.Http.PutAsJsonAsync("/api/v1/sso-admin/policy", new { localLoginEnabled = false, selfRegistrationEnabled = false }, Token);

        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(SsoErrors.LastMethod);
    }

    private static object Provider(string name, string clientId, bool enabled) => new
    {
        name,
        displayName = name,
        type = 0,
        discoveryUrl = "http://localhost:1/realms/x",
        clientId,
        clientSecret = "secret",
        isEnabled = enabled,
    };

    [Fact]
    public async Task Provider_changes_reach_the_running_scheme_without_a_restart()
    {
        var admin = await AdminAsync();
        var name = $"live{Random.Shared.Next(1000, 9999)}";
        var created = await admin.Http.PostAsJsonAsync("/api/v1/sso-admin/provider", Provider(name, "first", enabled: true), Token);
        var provider = (await created.Content.ReadFromJsonAsync<ExternalProviderDto>(Token))!;
        var registry = fixture.Factory.Services.GetRequiredService<ExternalSchemeRegistry>();
        var options = fixture.Factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        var schemes = fixture.Factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = ExternalSchemeRegistry.SchemeName(name);

        (await registry.EnsureAsync(name)).ShouldBeTrue();
        options.Get(scheme).ClientId.ShouldBe("first");

        (await admin.Http.PutAsJsonAsync($"/api/v1/sso-admin/{provider.Id}/provider", Provider(name, "second", enabled: true), Token)).EnsureSuccessStatusCode();
        (await registry.EnsureAsync(name)).ShouldBeTrue();
        options.Get(scheme).ClientId.ShouldBe("second");

        (await admin.Http.PutAsJsonAsync($"/api/v1/sso-admin/{provider.Id}/provider", Provider(name, "second", enabled: false), Token)).EnsureSuccessStatusCode();
        (await registry.EnsureAsync(name)).ShouldBeFalse();
        (await schemes.GetSchemeAsync(scheme)).ShouldBeNull();
    }

    [Fact]
    public async Task With_local_sign_in_off_only_admins_keep_the_password_route()
    {
        var admin = await AdminAsync();
        var (member, password) = await fixture.CreateUserAsync(Token);
        var created = await admin.Http.PostAsJsonAsync("/api/v1/sso-admin/provider", Provider($"off{Random.Shared.Next(1000, 9999)}", "client", enabled: true), Token);
        var provider = (await created.Content.ReadFromJsonAsync<ExternalProviderDto>(Token))!;
        await SetPolicyAsync(selfRegistration: false, local: false);
        try
        {
            var client = new AuthClient(fixture);
            var csrf = await client.GetCsrfTokenAsync(Token);
            var refused = await client.PostJsonAsync("/api/v1/auth/login", new { email = member.Email, password }, csrf, Token);
            (await refused.Content.ReadAsStringAsync(Token)).ShouldContain(SsoErrors.LocalDisabled);

            var again = new AuthClient(fixture);
            await again.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);

            // Switching off the provider now would leave nobody a way in.
            var last = await admin.Http.PutAsJsonAsync($"/api/v1/sso-admin/{provider.Id}/provider", Provider(provider.Name, "client", enabled: false), Token);
            (await last.Content.ReadAsStringAsync(Token)).ShouldContain(SsoErrors.LastMethod);
        }
        finally
        {
            await SetPolicyAsync(selfRegistration: false);
            (await admin.Http.PutAsJsonAsync($"/api/v1/sso-admin/{provider.Id}/provider", Provider(provider.Name, "client", enabled: false), Token)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Provider_secret_is_write_only_and_a_member_cannot_read_providers()
    {
        var admin = await AdminAsync();
        var created = await admin.Http.PostAsJsonAsync("/api/v1/sso-admin/provider", new
        {
            name = $"p{Random.Shared.Next(1000, 9999)}",
            displayName = "Corp",
            type = 0,
            discoveryUrl = "http://localhost:1/realms/x",
            clientId = "client",
            clientSecret = "top-secret",
            isEnabled = false,
        }, Token);

        var body = await created.Content.ReadAsStringAsync(Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldNotContain("top-secret");
        body.ShouldContain("\"hasSecret\":true");

        var member = await fixture.CreateUserClientAsync(Token);
        (await member.Http.GetAsync("/api/v1/sso-admin/providers", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_invites_a_new_email_and_the_person_gets_the_role()
    {
        var admin = await AdminAsync();
        var project = await ProjectAsync(admin);
        var email = NewEmail();

        var invited = await InviteAsync(admin, new { email, systemRole = "member", projects = new[] { new { projectId = project.Id, role = ProjectRoleNames.Viewer } } });
        (await AcceptAsync(TokenOf(invited.Link!))).EnsureSuccessStatusCode();

        var members = await admin.Http.GetFromJsonAsync<List<ProjectMemberDto>>($"/api/v1/project/{project.Id}/members", Token);
        members!.Single(member => member.User.Email == email).Role.ShouldBe(ProjectRoleNames.Viewer);

        var client = new AuthClient(fixture);
        await client.SignInAsync(email, "Accepted123!x", Token);
    }

    [Fact]
    public async Task An_invited_person_signing_in_with_the_idp_without_the_link_gets_the_invitation()
    {
        await SetPolicyAsync(selfRegistration: false);
        var admin = await AdminAsync();
        var project = await ProjectAsync(admin);
        var email = NewEmail();
        await InviteAsync(admin, new { email, projects = new[] { new { projectId = project.Id, role = ProjectRoleNames.Member } } });

        var pending = await admin.Http.GetFromJsonAsync<List<InvitationDto>>($"/api/v1/invitation/for-project/{project.Id}", Token);
        pending!.ShouldHaveSingleItem().Email.ShouldBe(email);

        (await LinkAsync(Identity(email, verified: false))).Error.ShouldBe(SsoErrors.RegistrationClosed);
        (await LinkAsync(Identity(email.ToUpperInvariant()))).Error.ShouldBeNull();

        var members = await admin.Http.GetFromJsonAsync<List<ProjectMemberDto>>($"/api/v1/project/{project.Id}/members", Token);
        members!.Single(member => member.User.Email == email).Role.ShouldBe(ProjectRoleNames.Member);
        (await admin.Http.GetFromJsonAsync<List<InvitationDto>>($"/api/v1/invitation/for-project/{project.Id}", Token))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_link_works_once_and_a_resend_retires_the_old_one()
    {
        var admin = await AdminAsync();
        var first = await InviteAsync(admin, new { email = NewEmail(), systemRole = "member" });
        var second = (await (await admin.Http.PostAsync($"/api/v1/invitation/{first.Invitation!.Id}/resend", null, Token)).Content.ReadFromJsonAsync<InvitationLinkDto>(Token))!;

        (await (await AcceptAsync(TokenOf(first.Link!))).Content.ReadAsStringAsync(Token)).ShouldContain(InvitationErrors.Invalid);
        (await AcceptAsync(TokenOf(second.Link!))).EnsureSuccessStatusCode();
        (await (await AcceptAsync(TokenOf(second.Link!))).Content.ReadAsStringAsync(Token)).ShouldContain(InvitationErrors.Invalid);
    }

    [Fact]
    public async Task Revoked_and_expired_links_are_refused()
    {
        var admin = await AdminAsync();
        var revoked = await InviteAsync(admin, new { email = NewEmail(), systemRole = "member" });
        (await admin.Http.PostAsync($"/api/v1/invitation/{revoked.Invitation!.Id}/revoke", null, Token)).EnsureSuccessStatusCode();
        (await (await AcceptAsync(TokenOf(revoked.Link!))).Content.ReadAsStringAsync(Token)).ShouldContain(InvitationErrors.Invalid);

        var expired = await InviteAsync(admin, new { email = NewEmail(), systemRole = "member" });
        await fixture.ExpireInvitationAsync(expired.Invitation!.Id);
        (await (await AcceptAsync(TokenOf(expired.Link!))).Content.ReadAsStringAsync(Token)).ShouldContain(InvitationErrors.Invalid);
    }

    [Fact]
    public async Task Sso_acceptance_needs_a_verified_matching_email_and_may_bypass_the_domain_list()
    {
        var admin = await AdminAsync();
        var email = NewEmail();
        var invited = await InviteAsync(admin, new { email, systemRole = "member" });
        var token = TokenOf(invited.Link!);

        (await LinkAsync(Identity(email, verified: false), token)).Error.ShouldBe(InvitationErrors.EmailMismatch);
        (await LinkAsync(Identity("someone-else@test.local"), token)).Error.ShouldBe(InvitationErrors.EmailMismatch);

        await SetPolicyAsync(selfRegistration: false);
        (await LinkAsync(Identity(email.ToUpperInvariant()), token)).Error.ShouldBeNull();
        (await LinkAsync(Identity(NewEmail()), token)).Error.ShouldBe(InvitationErrors.Invalid);
    }

    [Fact]
    public async Task Project_admin_is_limited_to_own_projects_and_own_role()
    {
        var admin = await AdminAsync();
        var foreign = await ProjectAsync(admin);
        var owner = await fixture.CreateUserClientAsync(Token);
        var own = await ProjectAsync(owner);
        var memberRole = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{own.Id}/member", new { userId = memberRole.UserId, role = ProjectRoleNames.Member }, Token)).EnsureSuccessStatusCode();

        var outside = await owner.Http.PostAsJsonAsync("/api/v1/invitation", new { email = NewEmail(), projects = new[] { new { projectId = foreign.Id, role = ProjectRoleNames.Member } } }, Token);
        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var system = await owner.Http.PostAsJsonAsync("/api/v1/invitation", new { email = NewEmail(), systemRole = "admin" }, Token);
        system.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var plain = await memberRole.Http.PostAsJsonAsync("/api/v1/invitation", new { email = NewEmail(), projects = new[] { new { projectId = own.Id, role = ProjectRoleNames.Viewer } } }, Token);
        plain.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await InviteAsync(owner, new { email = NewEmail(), projects = new[] { new { projectId = own.Id, role = ProjectRoleNames.Admin } } })).Link.ShouldNotBeNull();
    }

    [Fact]
    public async Task Project_invitation_gives_a_new_person_the_basic_system_role()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await ProjectAsync(owner);
        var email = NewEmail();
        var invited = await InviteAsync(owner, new { email, projects = new[] { new { projectId = project.Id, role = ProjectRoleNames.Member } } });

        (await AcceptAsync(TokenOf(invited.Link!))).EnsureSuccessStatusCode();

        var person = new AuthClient(fixture);
        await person.SignInAsync(email, "Accepted123!x", Token);
        (await person.Http.GetAsync("/api/v1/project", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Closed_invitations_stay_closed_and_a_copied_link_replaces_the_old_one()
    {
        var admin = await AdminAsync();
        var first = await InviteAsync(admin, new { email = NewEmail(), systemRole = "member" });
        var copied = (await (await admin.Http.PostAsync($"/api/v1/invitation/{first.Invitation!.Id}/link", null, Token)).Content.ReadFromJsonAsync<InvitationLinkDto>(Token))!;

        (await (await AcceptAsync(TokenOf(first.Link!))).Content.ReadAsStringAsync(Token)).ShouldContain(InvitationErrors.Invalid);
        (await AcceptAsync(TokenOf(copied.Link!))).EnsureSuccessStatusCode();

        var resend = await admin.Http.PostAsync($"/api/v1/invitation/{first.Invitation.Id}/resend", null, Token);
        (await resend.Content.ReadAsStringAsync(Token)).ShouldContain(InvitationErrors.Invalid);
    }

    [Fact]
    public async Task Existing_account_joins_the_project_directly()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await ProjectAsync(owner);
        var (user, _) = await fixture.CreateUserAsync(Token);

        var result = await InviteAsync(owner, new { email = user.Email.ToUpperInvariant(), projects = new[] { new { projectId = project.Id, role = ProjectRoleNames.Member } } });

        result.AddedDirectly.ShouldBeTrue();
        result.Link.ShouldBeNull();
        var members = await owner.Http.GetFromJsonAsync<List<ProjectMemberDto>>($"/api/v1/project/{project.Id}/members", Token);
        members!.ShouldContain(member => member.User.Id == user.Id);
    }

    [Fact]
    public async Task Failed_email_keeps_the_invitation_and_shows_the_status()
    {
        var admin = await AdminAsync();

        var invited = await InviteAsync(admin, new { email = ScriptedEmailSender.BouncePrefix + NewEmail(), systemRole = "member" });
        var delivered = await InviteAsync(admin, new { email = NewEmail(), systemRole = "member" });

        await WaitForEmailStatusAsync(admin, invited.Invitation!.Id, InvitationEmailStatus.Failed);
        await WaitForEmailStatusAsync(admin, delivered.Invitation!.Id, InvitationEmailStatus.Sent);

        (await AcceptAsync(TokenOf(invited.Link!))).EnsureSuccessStatusCode();
    }

    private static async Task WaitForEmailStatusAsync(AuthClient admin, Guid id, InvitationEmailStatus expected)
    {
        var status = InvitationEmailStatus.Queued;
        for (var wait = 0; wait < 100 && status != expected; wait++)
        {
            await Task.Delay(300, Token);
            var list = await admin.Http.GetFromJsonAsync<PagedResultDto<InvitationDto>>("/api/v1/invitation?maxResultCount=1000", Token);
            status = list!.Items.Single(item => item.Id == id).EmailStatus;
        }

        status.ShouldBe(expected);
    }
}
