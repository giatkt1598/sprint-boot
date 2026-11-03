using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_space_admin_uploads_a_logo_that_shows_wherever_the_space_does()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var key = $"L{Random.Shared.Next(100000, 999999)}";
        var space = (await (await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Logo", key, type = 0 }, Token)).Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

        using (var svg = new MultipartFormDataContent { { new ByteArrayContent("<svg/>"u8.ToArray()), "file", "a.svg" } })
        {
            (await (await owner.Http.PostAsync($"/api/v1/project/{space.Id}/upload-logo", svg, Token)).Content.ReadAsStringAsync(Token)).ShouldContain(ProjectErrors.LogoInvalid);
        }

        using var form = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "logo.png" } };
        var uploaded = await owner.Http.PostAsync($"/api/v1/project/{space.Id}/upload-logo", form, Token);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync(Token));
        var url = (await uploaded.Content.ReadFromJsonAsync<ProjectDto>(Token))!.LogoUrl!;

        (await fixture.Factory.CreateClient().GetAsync(url, Token)).Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await owner.Http.GetFromJsonAsync<List<ProjectDto>>("/api/v1/project", Token))!.Single(item => item.Id == space.Id).LogoUrl.ShouldBe(url);
        (await owner.Http.GetFromJsonAsync<List<UserSpaceDto>>("/api/v1/project/my-spaces", Token))!.Single().LogoUrl.ShouldBe(url);

        var stranger = await fixture.CreateUserClientAsync(Token);
        using var other = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "logo.png" } };
        (await stranger.Http.PostAsync($"/api/v1/project/{space.Id}/upload-logo", other, Token)).IsSuccessStatusCode.ShouldBeFalse();

        (await owner.Http.DeleteAsync($"/api/v1/project/{space.Id}/logo", Token)).EnsureSuccessStatusCode();
        (await fixture.Factory.CreateClient().GetAsync(url, Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task People_see_their_own_spaces_and_roles()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var key = $"Y{Random.Shared.Next(100000, 999999)}";
        (await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Mine", key, type = 0 }, Token)).EnsureSuccessStatusCode();

        var spaces = (await client.Http.GetFromJsonAsync<List<UserSpaceDto>>("/api/v1/project/my-spaces", Token))!;
        var mine = spaces.ShouldHaveSingleItem();
        mine.Key.ShouldBe(key);
        mine.Role.ShouldBe(ProjectRoleNames.Admin);

        var stranger = await fixture.CreateUserClientAsync(Token);
        (await stranger.Http.GetFromJsonAsync<List<UserSpaceDto>>("/api/v1/project/my-spaces", Token))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_profile_picture_is_uploaded_served_to_anyone_and_removed()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

        using var refused = new MultipartFormDataContent { { new ByteArrayContent("<svg/>"u8.ToArray()), "file", "a.svg" } };
        var rejected = await client.Http.PostAsync("/api/v1/auth/avatar", refused, Token);
        rejected.IsSuccessStatusCode.ShouldBeFalse();
        (await rejected.Content.ReadAsStringAsync(Token)).ShouldContain(AuthErrors.AvatarInvalid);

        using var form = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "a.png" } };
        var uploaded = await client.Http.PostAsync("/api/v1/auth/avatar", form, Token);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync(Token));
        var url = (await uploaded.Content.ReadFromJsonAsync<CurrentUserDto>(Token))!.AvatarUrl!;

        var served = await fixture.Factory.CreateClient().GetAsync(url, Token);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await client.Http.GetFromJsonAsync<List<UserRefDto>>("/api/v1/user-directory/search?search=" + Uri.EscapeDataString((await client.Http.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Token))!.Email), Token))!
            .ShouldHaveSingleItem().AvatarUrl.ShouldBe(url);

        (await client.Http.DeleteAsync("/api/v1/auth/avatar", Token)).EnsureSuccessStatusCode();
        (await fixture.Factory.CreateClient().GetAsync(url, Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Login_without_csrf_token_is_rejected()
    {
        var client = new AuthClient(fixture);

        var response = await client.PostLoginAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, csrf: null, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Authorization_code_flow_with_pkce_issues_tokens_that_call_the_api()
    {
        var client = new AuthClient(fixture);

        await client.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);

        var me = await client.Http.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Token);
        me!.Email.ShouldBe(ApiFixture.AdminEmail);
    }

    [Fact]
    public async Task Api_without_token_returns_401()
    {
        var response = await new AuthClient(fixture).Http.GetAsync("/api/v1/auth/me", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_token_rotates()
    {
        var client = new AuthClient(fixture);
        await client.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var first = client.RefreshToken!;

        await client.RefreshAsync(Token);

        client.RefreshToken.ShouldNotBe(first);
        (await client.Http.GetAsync("/api/v1/auth/me", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_look_identical()
    {
        var client = new AuthClient(fixture);
        var csrf = await client.GetCsrfTokenAsync(Token);

        var unknown = await client.PostLoginAsync("nobody@test.local", "whatever", csrf, Token);
        var wrong = await client.PostLoginAsync("nobody2@test.local", "whatever", csrf, Token);

        unknown.StatusCode.ShouldBe(wrong.StatusCode);
        (await unknown.Content.ReadAsStringAsync(Token)).ShouldBe(await wrong.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Logout_revocation_kills_the_refresh_token()
    {
        var client = new AuthClient(fixture);
        await client.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);

        var revoke = await client.PostFormAsync("/connect/revocation", new Dictionary<string, string>
        {
            ["client_id"] = "sprint-boot-web",
            ["token"] = client.RefreshToken!,
            ["token_type_hint"] = "refresh_token",
        }, Token);
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refresh = await client.PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "sprint-boot-web",
            ["refresh_token"] = client.RefreshToken!,
        }, Token);
        refresh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Member_calling_an_admin_api_gets_403()
    {
        var member = await fixture.CreateUserClientAsync(Token);

        var response = await member.Http.GetAsync("/api/v1/user-admin?maxResultCount=10", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Forgotten_password_flow_resets_with_a_single_use_token()
    {
        var (user, password) = await fixture.CreateUserAsync(Token);
        var client = new AuthClient(fixture);
        var token = await fixture.GenerateResetTokenAsync(user.Id);
        var csrf = await client.GetCsrfTokenAsync(Token);

        var first = await client.PostJsonAsync("/api/v1/auth/reset-password", new { userId = user.Id, token, newPassword = password + "x" }, csrf, Token);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await client.PostJsonAsync("/api/v1/auth/reset-password", new { userId = user.Id, token, newPassword = password + "y" }, csrf, Token);
        (await second.Content.ReadAsStringAsync(Token)).ShouldContain(AuthErrors.ResetLinkInvalid);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var (user, password) = await fixture.CreateUserAsync(Token);
        var client = new AuthClient(fixture);
        var csrf = await client.GetCsrfTokenAsync(Token);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostLoginAsync(user.Email, "wrong", csrf, Token);
        }

        var response = await client.PostLoginAsync(user.Email, password, csrf, Token);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(AuthErrors.LockedOut);
    }

    [Fact]
    public async Task Admin_deletes_an_account_whose_work_then_shows_a_deactivated_user()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var gone = await fixture.CreateUserClientAsync(Token);
        var key = $"D{Random.Shared.Next(100000, 999999)}";
        var space = (await (await admin.Http.PostAsJsonAsync("/api/v1/project", new { name = "Del", key, type = 0 }, Token)).Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        (await admin.Http.PostAsJsonAsync($"/api/v1/project/{space.Id}/member", new { userId = gone.UserId, role = ProjectRoleNames.Member }, Token)).EnsureSuccessStatusCode();
        var types = (await admin.Http.GetFromJsonAsync<List<SprintBoot.Issues.IssueTypeDto>>($"/api/v1/issue/types/{space.Id}", Token))!;
        var issue = (await (await admin.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = space.Id, typeId = types.Single(type => type.Name == "Task").Id, title = "Theirs", assigneeId = gone.UserId }, Token)).Content.ReadFromJsonAsync<SprintBoot.Issues.IssueDto>(Token))!;

        var me = (await admin.Http.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Token))!;
        var self = await admin.Http.DeleteAsync($"/api/v1/user-admin/{me.Id}", Token);
        (await self.Content.ReadAsStringAsync(Token)).ShouldContain(AuthErrors.CannotDeleteSelf);
        (await admin.Http.DeleteAsync($"/api/v1/user-admin/{gone.UserId}", Token)).EnsureSuccessStatusCode();

        var read = (await admin.Http.GetFromJsonAsync<SprintBoot.Issues.IssueDto>($"/api/v1/issue/by-key?key={issue.Key}", Token))!;
        read.Assignee!.Name.ShouldBe(UserDirectory.DeactivatedName);
        read.Assignee.IsDeactivated.ShouldBeTrue();
        read.Assignee.Email.ShouldBeEmpty();

        (await admin.Http.GetAsync($"/api/v1/user-admin/{gone.UserId}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await gone.Http.GetAsync("/api/v1/auth/me", Token)).IsSuccessStatusCode.ShouldBeFalse();

        // Memberships go through the outbox, so they leave shortly after.
        for (var wait = 0; wait < 50; wait++)
        {
            var members = (await admin.Http.GetFromJsonAsync<List<ProjectMemberDto>>($"/api/v1/project/{space.Id}/members", Token))!;
            if (members.All(member => member.User.Id != gone.UserId))
            {
                return;
            }

            await Task.Delay(200, Token);
        }

        throw new TimeoutException("The deleted user is still a member.");
    }

    [Fact]
    public async Task Admin_sees_spaces_and_sign_in_methods_of_a_user_but_a_member_cannot()
    {
        var member = await fixture.CreateUserClientAsync(Token);
        var created = await member.Http.PostAsJsonAsync(
            "/api/v1/project", new { name = "Mine", key = $"M{Random.Shared.Next(100000, 999999)}", type = 0 }, Token);
        created.EnsureSuccessStatusCode();
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);

        var spaces = await admin.Http.GetFromJsonAsync<List<UserSpaceDto>>(
            $"/api/v1/project-admin/user-spaces/{member.UserId}", Token);
        spaces!.Single().Role.ShouldBe(ProjectRoleNames.Admin);

        var methods = await admin.Http.GetFromJsonAsync<List<SignInMethodDto>>(
            $"/api/v1/user-admin/{member.UserId}/sign-in-methods", Token);
        methods!.Single().Method.ShouldBe(UserAdminAppService.PasswordMethod);

        (await member.Http.GetAsync($"/api/v1/project-admin/user-spaces/{member.UserId}", Token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.Http.GetAsync($"/api/v1/user-admin/{member.UserId}/sign-in-methods", Token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
