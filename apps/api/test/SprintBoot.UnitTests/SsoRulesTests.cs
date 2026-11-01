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

    [Fact]
    public void Invitation_tokens_are_random_and_hashed()
    {
        var token = InvitationToken.NewToken();

        token.ShouldNotBe(InvitationToken.NewToken());
        InvitationToken.Hash(token).ShouldNotContain(token);
        InvitationToken.Hash(token).Length.ShouldBe(64);
    }
}
