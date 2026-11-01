using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Identity;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

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
}
