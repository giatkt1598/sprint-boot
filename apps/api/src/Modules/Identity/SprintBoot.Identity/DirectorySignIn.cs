using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;

namespace SprintBoot.Identity;

/// <summary>Reads what a SAML identity provider publishes about itself.</summary>
public static class SamlMetadata
{
    /// <summary>The entityID of the metadata at the address; throws when it cannot be read.</summary>
    public static async Task<string> ReadEntityIdAsync(HttpClient client, string url, CancellationToken cancellation = default)
    {
        await using var stream = await client.GetStreamAsync(url, cancellation);
        // Metadata never needs a DTD; refusing one keeps entity expansion out.
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, Async = true });
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellation);
        var root = document.Root ?? throw new XmlException("Empty metadata");

        // Federation metadata wraps several entities; the first identity provider is the one used.
        var entity = root.Name.LocalName == "EntityDescriptor"
            ? root
            : root.Descendants().FirstOrDefault(item => item.Name.LocalName == "EntityDescriptor")
                ?? throw new XmlException("No EntityDescriptor in metadata");
        return entity.Attribute("entityID")?.Value is { Length: > 0 } id ? id : throw new XmlException("EntityDescriptor without entityID");
    }
}

/// <summary>Where an LDAP provider keeps what does not fit the shared provider columns.</summary>
public static class LdapSettings
{
    public const string SearchBaseProperty = "LdapSearchBase";
    public const string UserFilterProperty = "LdapUserFilter";

    /// <summary>Matches OpenLDAP and Active Directory accounts by login name or email.</summary>
    public const string DefaultUserFilter = "(|(uid={0})(sAMAccountName={0})(mail={0}))";

    public const string DefaultEmailAttribute = "mail";
    public const string DefaultNameAttribute = "displayName";

    public static string? SearchBase(ExternalProviderConfig provider) => provider.GetProperty<string?>(SearchBaseProperty);

    public static string UserFilter(ExternalProviderConfig provider) =>
        provider.GetProperty<string?>(UserFilterProperty) is { Length: > 0 } filter ? filter : DefaultUserFilter;

    public static void Set(ExternalProviderConfig provider, string? searchBase, string? userFilter)
    {
        provider.SetProperty(SearchBaseProperty, string.IsNullOrWhiteSpace(searchBase) ? null : searchBase.Trim());
        provider.SetProperty(UserFilterProperty, string.IsNullOrWhiteSpace(userFilter) ? null : userFilter.Trim());
    }
}

public record LdapServer(string Address, string BindDn, string? BindPassword);

/// <summary>Checks a username and password against a directory, through a service account that finds the person.</summary>
public class LdapAuthenticator(ILogger<LdapAuthenticator> logger) : ITransientDependency
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly string[] SubjectAttributes = ["objectGUID", "entryUUID"];

    /// <summary>The person's identity, or null when the username or password is wrong.</summary>
    public async Task<ExternalIdentity?> AuthenticateAsync(ExternalProviderConfig provider, string? bindPassword, string userName, string password)
    {
        // An empty password is an anonymous bind, which most directories accept: it proves nothing.
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var server = new LdapServer(provider.DiscoveryUrl ?? string.Empty, provider.ClientId, bindPassword);
        var emailAttribute = string.IsNullOrWhiteSpace(provider.EmailClaim) ? LdapSettings.DefaultEmailAttribute : provider.EmailClaim;
        var nameAttribute = string.IsNullOrWhiteSpace(provider.NameClaim) ? LdapSettings.DefaultNameAttribute : provider.NameClaim;

        return await Task.Run(() =>
        {
            try
            {
                using var service = Connect(server);
                service.Bind(new NetworkCredential(server.BindDn, server.BindPassword));

                var filter = string.Format(LdapSettings.UserFilter(provider), Escape(userName.Trim()));
                var request = new SearchRequest(LdapSettings.SearchBase(provider), filter, SearchScope.Subtree,
                    [.. SubjectAttributes, emailAttribute, nameAttribute, "cn"])
                {
                    SizeLimit = 2,
                };
                var entries = ((SearchResponse)service.SendRequest(request)).Entries;
                if (entries.Count != 1)
                {
                    // None, or a filter so loose that it cannot say who this is.
                    return null;
                }

                var entry = entries[0];
                using var person = Connect(server);
                person.Bind(new NetworkCredential(entry.DistinguishedName, password));

                return new ExternalIdentity(
                    provider.Name,
                    provider.DisplayName,
                    Subject(entry),
                    Read(entry, emailAttribute),
                    // Directory accounts are created by the organization, so their email is taken as verified.
                    EmailVerified: true,
                    Read(entry, nameAttribute) ?? Read(entry, "cn"));
            }
            catch (LdapException failure) when (failure.ErrorCode == InvalidCredentials)
            {
                return null;
            }
            catch (Exception failure) when (failure is LdapException or DirectoryOperationException or UriFormatException)
            {
                logger.LogWarning(failure, "LDAP sign-in through {Provider} failed", provider.Name);
                throw new BusinessException(SsoErrors.ExternalFailed);
            }
        });
    }

    /// <summary>Binds with the service account; returns the problem, or null when it works.</summary>
    public async Task<string?> TestAsync(LdapServer server) => await Task.Run(() =>
    {
        try
        {
            using var connection = Connect(server);
            connection.Bind(new NetworkCredential(server.BindDn, server.BindPassword));
            return null;
        }
        catch (Exception failure) when (failure is LdapException or DirectoryOperationException or UriFormatException)
        {
            logger.LogInformation(failure, "LDAP test bind to {Address} failed", server.Address);
            return failure.Message;
        }
    });

    private const int InvalidCredentials = 49;

    private static LdapConnection Connect(LdapServer server)
    {
        var uri = new Uri(server.Address);
        var secure = uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase);
        if (!secure && !uri.Scheme.Equals("ldap", StringComparison.OrdinalIgnoreCase))
        {
            throw new UriFormatException("LDAP addresses start with ldap:// or ldaps://");
        }

        var port = uri.IsDefaultPort || uri.Port <= 0 ? (secure ? 636 : 389) : uri.Port;
        var connection = new LdapConnection(new LdapDirectoryIdentifier(uri.Host, port)) { AuthType = AuthType.Basic, Timeout = Timeout };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = secure;
        return connection;
    }

    /// <summary>A stable id survives renames: objectGUID on Active Directory, entryUUID elsewhere, the DN at worst.</summary>
    private static string Subject(SearchResultEntry entry)
    {
        if (entry.Attributes["objectGUID"] is { Count: > 0 } guid && guid[0] is byte[] { Length: 16 } bytes)
        {
            return new Guid(bytes).ToString();
        }

        return Read(entry, "entryUUID") ?? entry.DistinguishedName;
    }

    private static string? Read(SearchResultEntry entry, string attribute) =>
        entry.Attributes[attribute] is { Count: > 0 } values
            ? values[0] switch
            {
                string text => text,
                byte[] raw => Encoding.UTF8.GetString(raw),
                _ => null,
            }
            : null;

    /// <summary>RFC 4515: what someone types must never change the filter around it.</summary>
    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => character.ToString(),
            });
        }

        return builder.ToString();
    }
}
