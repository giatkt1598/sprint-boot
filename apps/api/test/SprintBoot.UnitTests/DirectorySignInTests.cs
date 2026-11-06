using System.Net;
using System.Text;
using System.Xml;
using Shouldly;
using SprintBoot.Identity;
using Xunit;

namespace SprintBoot.UnitTests;

public class DirectorySignInTests
{
    private sealed class StaticHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/xml") });
    }

    private static Task<string> EntityIdAsync(string xml) =>
        SamlMetadata.ReadEntityIdAsync(new HttpClient(new StaticHandler(xml)), "https://idp.test/metadata");

    [Fact]
    public async Task The_entity_id_comes_from_the_entity_descriptor()
    {
        (await EntityIdAsync("""<md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" entityID="https://idp.test/realms/corp"><md:IDPSSODescriptor /></md:EntityDescriptor>"""))
            .ShouldBe("https://idp.test/realms/corp");
    }

    [Fact]
    public async Task Federation_metadata_uses_its_first_entity()
    {
        (await EntityIdAsync("""<EntitiesDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata"><EntityDescriptor entityID="first" /><EntityDescriptor entityID="second" /></EntitiesDescriptor>"""))
            .ShouldBe("first");
    }

    [Fact]
    public async Task Metadata_with_a_dtd_is_refused()
    {
        await Should.ThrowAsync<XmlException>(() => EntityIdAsync("""<!DOCTYPE x [<!ENTITY a "b">]><EntityDescriptor entityID="&a;" />"""));
    }

    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("*", @"\2a")]
    [InlineData("a)(uid=*", @"a\29\28uid=\2a")]
    [InlineData(@"back\slash", @"back\5cslash")]
    public void Typed_names_cannot_change_the_ldap_filter(string typed, string escaped)
    {
        LdapAuthenticator.Escape(typed).ShouldBe(escaped);
    }
}
