using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Shouldly;
using SprintBoot.Identity;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TwoFactorTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_turns_on_an_authenticator_app_and_then_signs_in_with_its_code()
    {
        var (user, password) = await fixture.CreateUserAsync(Token);
        var member = new AuthClient(fixture) { UserId = user.Id };
        await member.SignInAsync(user.Email, password, Token);

        var setup = (await member.Http.GetFromJsonAsync<TwoFactorSetupDto>("/api/v1/auth/two-factor/setup", Token))!;
        setup.AuthenticatorUri.ShouldStartWith("otpauth://totp/Sprint%20Boot:");
        setup.AuthenticatorUri.ShouldContain($"secret={setup.SharedKey}");
        // The same key until setup finishes, so a reload does not undo a scanned app.
        (await member.Http.GetFromJsonAsync<TwoFactorSetupDto>("/api/v1/auth/two-factor/setup", Token))!.SharedKey.ShouldBe(setup.SharedKey);

        (await (await member.Http.PostAsJsonAsync("/api/v1/auth/two-factor/setup", new { code = "000000" }, Token)).Content.ReadAsStringAsync(Token))
            .ShouldContain(AuthErrors.TwoFactorCodeInvalid);
        var enabled = await member.Http.PostAsJsonAsync("/api/v1/auth/two-factor/setup", new { code = Totp.Code(setup.SharedKey) }, Token);
        enabled.StatusCode.ShouldBe(HttpStatusCode.OK, await enabled.Content.ReadAsStringAsync(Token));
        var recovery = (await enabled.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesDto>(Token))!.RecoveryCodes;
        recovery.Count.ShouldBe(10);
        (await member.Http.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Token))!.TwoFactorEnabled.ShouldBeTrue();

        // The password alone no longer signs in: the authorize endpoint still sends the browser to the login page.
        var next = new AuthClient(fixture);
        var csrf = await next.GetCsrfTokenAsync(Token);
        (await ReadStepAsync(await next.PostLoginAsync(user.Email, password, csrf, Token))).ShouldBe(LoginSteps.Code);
        var wrong = await next.PostJsonAsync("/api/v1/auth/two-factor", new { code = "123456" }, csrf, Token);
        (await wrong.Content.ReadAsStringAsync(Token)).ShouldContain(AuthErrors.TwoFactorCodeInvalid);
        var verified = await next.PostJsonAsync("/api/v1/auth/two-factor", new { code = Totp.Code(setup.SharedKey) }, csrf, Token);
        verified.StatusCode.ShouldBe(HttpStatusCode.NoContent, await verified.Content.ReadAsStringAsync(Token));
        await next.AuthorizeAsync(Token);
        (await next.Http.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Token))!.Id.ShouldBe(user.Id);

        // A recovery code works once, typed in lower case without its dash.
        var lost = new AuthClient(fixture);
        csrf = await lost.GetCsrfTokenAsync(Token);
        (await ReadStepAsync(await lost.PostLoginAsync(user.Email, password, csrf, Token))).ShouldBe(LoginSteps.Code);
        var typed = recovery[0].Replace("-", string.Empty).ToLowerInvariant();
        (await lost.PostJsonAsync("/api/v1/auth/two-factor", new { code = typed, isRecoveryCode = true }, csrf, Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await lost.AuthorizeAsync(Token);

        var again = new AuthClient(fixture);
        csrf = await again.GetCsrfTokenAsync(Token);
        await again.PostLoginAsync(user.Email, password, csrf, Token);
        (await (await again.PostJsonAsync("/api/v1/auth/two-factor", new { code = recovery[0], isRecoveryCode = true }, csrf, Token)).Content.ReadAsStringAsync(Token))
            .ShouldContain(AuthErrors.TwoFactorCodeInvalid);

        // Turning it off needs a current code; then the password is enough again.
        (await (await member.Http.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { code = "000000" }, Token)).Content.ReadAsStringAsync(Token))
            .ShouldContain(AuthErrors.TwoFactorCodeInvalid);
        (await member.Http.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { code = Totp.Code(setup.SharedKey) }, Token)).EnsureSuccessStatusCode();
        await new AuthClient(fixture).SignInAsync(user.Email, password, Token);
    }

    [Fact]
    public async Task A_code_without_a_held_sign_in_is_refused()
    {
        var client = new AuthClient(fixture);
        var csrf = await client.GetCsrfTokenAsync(Token);
        (await (await client.PostJsonAsync("/api/v1/auth/two-factor", new { code = "123456" }, csrf, Token)).Content.ReadAsStringAsync(Token))
            .ShouldContain(AuthErrors.TwoFactorExpired);
        (await client.Http.GetAsync("/api/v1/auth/two-factor/held-setup", Token)).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task When_the_workspace_requires_two_factor_members_set_it_up_before_they_get_in()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var (user, password) = await fixture.CreateUserAsync(Token);
        var member = new AuthClient(fixture);
        await member.SignInAsync(user.Email, password, Token);

        await SetRequiredAsync(admin, true);
        try
        {
            (await admin.Http.GetFromJsonAsync<AuthPolicyDto>("/api/v1/sso-admin/policy", Token))!.RequireTwoFactor.ShouldBeTrue();
            // Signed in before the switch: the app asks for setup before anything else.
            var me = (await member.Http.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Token))!;
            me.TwoFactorRequired.ShouldBeTrue();
            me.TwoFactorEnabled.ShouldBeFalse();

            var client = new AuthClient(fixture);
            var csrf = await client.GetCsrfTokenAsync(Token);
            (await ReadStepAsync(await client.PostLoginAsync(user.Email, password, csrf, Token))).ShouldBe(LoginSteps.Setup);

            // Held for setup: no access yet, and no code can skip setting it up.
            var authorize = await client.Http.GetAsync("/connect/authorize?client_id=sprint-boot-web&redirect_uri=http%3A%2F%2Flocalhost%3A5173%2Fauth%2Fcallback&response_type=code&scope=openid&code_challenge=x&code_challenge_method=S256", Token);
            authorize.Headers.Location?.ToString().ShouldNotStartWith(AuthClient.RedirectUri);
            (await (await client.PostJsonAsync("/api/v1/auth/two-factor", new { code = "123456" }, csrf, Token)).Content.ReadAsStringAsync(Token))
                .ShouldContain(AuthErrors.TwoFactorNotEnabled);

            var setup = (await client.Http.GetFromJsonAsync<TwoFactorSetupDto>("/api/v1/auth/two-factor/held-setup", Token))!;
            var done = await client.PostJsonAsync("/api/v1/auth/two-factor/held-setup", new { code = Totp.Code(setup.SharedKey) }, csrf, Token);
            done.StatusCode.ShouldBe(HttpStatusCode.OK, await done.Content.ReadAsStringAsync(Token));
            (await done.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesDto>(Token))!.RecoveryCodes.Count.ShouldBe(10);
            await client.AuthorizeAsync(Token);
            (await client.Http.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me", Token))!.TwoFactorEnabled.ShouldBeTrue();

            // The policy keeps it on.
            (await (await client.Http.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { code = Totp.Code(setup.SharedKey) }, Token)).Content.ReadAsStringAsync(Token))
                .ShouldContain(AuthErrors.TwoFactorRequiredByPolicy);

            // A lost phone: an admin resets it, and the next sign-in sets up a new app.
            var reset = (await (await admin.Http.PostAsync($"/api/v1/user-admin/{user.Id}/reset-two-factor", null, Token)).Content.ReadFromJsonAsync<UserSummaryDto>(Token))!;
            reset.TwoFactorEnabled.ShouldBeFalse();
            var afterReset = new AuthClient(fixture);
            csrf = await afterReset.GetCsrfTokenAsync(Token);
            (await ReadStepAsync(await afterReset.PostLoginAsync(user.Email, password, csrf, Token))).ShouldBe(LoginSteps.Setup);
            (await afterReset.Http.GetFromJsonAsync<TwoFactorSetupDto>("/api/v1/auth/two-factor/held-setup", Token))!.SharedKey.ShouldNotBe(setup.SharedKey);
        }
        finally
        {
            await SetRequiredAsync(admin, false);
        }
    }

    private static async Task SetRequiredAsync(AuthClient admin, bool required)
    {
        var policy = (await admin.Http.GetFromJsonAsync<AuthPolicyDto>("/api/v1/sso-admin/policy", Token))!;
        (await admin.Http.PutAsJsonAsync("/api/v1/sso-admin/policy", new
        {
            localLoginEnabled = policy.LocalLoginEnabled,
            selfRegistrationEnabled = policy.SelfRegistrationEnabled,
            allowedDomains = policy.AllowedDomains,
            requireTwoFactor = required,
        }, Token)).EnsureSuccessStatusCode();
    }

    private static async Task<string> ReadStepAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<LoginResultDto>(Token))!.Step;
    }
}

/// <summary>What an authenticator app shows: RFC 6238 with SHA-1, 30-second steps and 6 digits.</summary>
public static class Totp
{
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var step = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        var hash = HMACSHA1.HashData(Base32(base32Key), counter);
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    private static byte[] Base32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var character in text.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
