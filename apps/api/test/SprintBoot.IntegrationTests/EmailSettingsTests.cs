using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Identity;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class EmailSettingsTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_saves_smtp_settings_and_the_password_stays_on_the_server()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var input = new { host = "smtp.test.local", port = 2525, enableSsl = true, userName = "mailer", password = "s3cret-value", fromAddress = "team@test.local", fromName = "Team" };

        var saved = await admin.Http.PutAsJsonAsync("/api/v1/email-settings", input, Token);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        (await saved.Content.ReadAsStringAsync(Token)).ShouldNotContain("s3cret-value");

        var read = await admin.Http.GetFromJsonAsync<EmailSettingsDto>("/api/v1/email-settings", Token);
        read!.Host.ShouldBe("smtp.test.local");
        read.Port.ShouldBe(2525);
        read.EnableSsl.ShouldBeTrue();
        read.HasPassword.ShouldBeTrue();
        read.FromAddress.ShouldBe("team@test.local");

        var keepPassword = new { host = "smtp.test.local", port = 2525, enableSsl = true, userName = "mailer", password = "", fromAddress = "team@test.local", fromName = "Team" };
        (await admin.Http.PutAsJsonAsync("/api/v1/email-settings", keepPassword, Token)).EnsureSuccessStatusCode();
        (await admin.Http.GetFromJsonAsync<EmailSettingsDto>("/api/v1/email-settings", Token))!.HasPassword.ShouldBeTrue();

        (await admin.Http.PostAsJsonAsync("/api/v1/email-settings/send-test", new { to = "someone@test.local" }, Token)).EnsureSuccessStatusCode();

        var cleared = new { host = "localhost", port = 1025, enableSsl = false, userName = "", password = "", fromAddress = "no-reply@test.local", fromName = "Sprint Boot" };
        (await admin.Http.PutAsJsonAsync("/api/v1/email-settings", cleared, Token)).EnsureSuccessStatusCode();
        (await admin.Http.GetFromJsonAsync<EmailSettingsDto>("/api/v1/email-settings", Token))!.HasPassword.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_a_manager_of_email_settings_can_read_them()
    {
        var member = await fixture.CreateUserClientAsync(Token);
        (await member.Http.GetAsync("/api/v1/email-settings", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
