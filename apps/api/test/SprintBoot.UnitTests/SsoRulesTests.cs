using System.Text.Json;
using Shouldly;
using SprintBoot.Identity;
using Xunit;

namespace SprintBoot.UnitTests;

public class SsoRulesTests
{
    [Theory]
    [InlineData("a@corp.com", "corp.com", true)]
    [InlineData("a@CORP.com", "corp.com, other.io", true)]
    [InlineData("a@evil.com", "corp.com", false)]
    [InlineData("a@corp.com.evil.io", "corp.com", false)]
    [InlineData("a@any.io", "", true)]
    public void Domain_rule_matches_the_whole_domain(string email, string allowed, bool expected)
    {
        ExternalAccountLinker.DomainAllowed(email, allowed).ShouldBe(expected);
    }

    [Fact]
    public void GitHub_primary_email_keeps_its_verified_flag()
    {
        var emails = JsonDocument.Parse("""
            [{"email":"old@x.io","primary":false,"verified":true},{"email":"me@x.io","primary":true,"verified":false}]
            """).RootElement;

        GitHubEmails.Pick(emails).ShouldBe(("me@x.io", false));
    }

    [Theory]
    [InlineData("https://id.corp.com/realms/x/.well-known/openid-configuration", "https://id.corp.com/realms/x")]
    [InlineData("https://id.corp.com/realms/x/", "https://id.corp.com/realms/x")]
    public void Discovery_address_becomes_an_authority(string url, string expected)
    {
        ExternalSchemeRegistry.ToAuthority(url).ShouldBe(expected);
    }

    [Fact]
    public void Invitation_tokens_are_random_and_hashed()
    {
        var token = InvitationToken.NewToken();

        token.ShouldNotBe(InvitationToken.NewToken());
        InvitationToken.Hash(token).ShouldNotContain(token);
        InvitationToken.Hash(token).Length.ShouldBe(64);
    }
}
