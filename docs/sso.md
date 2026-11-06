# Single sign-on

Workspace admins connect identity providers under **Admin → Single sign-on**. Every provider signs people
into the same Sprint Boot account: a known login is used directly, a verified email that matches an
account is linked to it, and anyone else is created when the sign-in policy allows self-registration or
they hold an invitation.

| Type | How people sign in | Email treated as verified |
| --- | --- | --- |
| OpenID Connect | Redirect to the provider | Only when it sends `email_verified=true` |
| Google, GitHub | Redirect to the provider | Only when the provider says so |
| SAML 2.0 | Redirect to the provider | When **Trust email** is on (the default) |
| LDAP / Active Directory | Username and password on the Sprint Boot sign-in page | When **Trust email** is on (the default) |

> **Trust email.** SAML and LDAP carry no "verified" flag, so each such provider has a **Trust email**
> switch. On, Sprint Boot links the email they send to an existing account with the same address, and
> accepts it for invitations and self-registration. Leave it on only for an IdP or a directory whose people
> cannot change their own email; otherwise someone could sign in as another person who uses that address.
> Off, the provider signs in only people who already linked it from their profile.

All addresses below assume Sprint Boot is served at `https://sprint.example.com` and the provider is named
`corp`. The admin page shows the real values to copy.

## OpenID Connect

Register a confidential client at the provider with the redirect URL
`https://sprint.example.com/connect/signin-corp`, then enter its issuer (or discovery URL), client ID and
client secret.

## SAML 2.0

1. In Sprint Boot, add a **SAML 2.0** provider. Enter the IdP metadata URL, such as
   `https://idp.example.com/realms/corp/protocol/saml/descriptor`. The service provider entity ID defaults to
   our metadata URL; any unique value works if the IdP needs another.
2. At the IdP, create a SAML client (relying party) with:
   - Entity ID: the service provider entity ID above.
   - Assertion consumer service (HTTP-POST): `https://sprint.example.com/connect/signin-corp/Acs`.
   - Service provider metadata, if the IdP can import it: `https://sprint.example.com/connect/signin-corp`.
   - A persistent NameID, and attributes for the email and the display name.
3. Set **Attribute mapping** to the attribute names the IdP sends, for example `email=email, name=name`.
   Without a match Sprint Boot also tries the standard email claim types.
4. **Test connection** reads the metadata and shows the IdP entity ID.

Sprint Boot does not sign its authentication requests, so turn off "client signature required" or the
equivalent at the IdP. Responses must be signed. Only sign-ins Sprint Boot started are accepted, not
IdP-initiated ones.

Behind a reverse proxy, set `Auth:SpaBaseUrl` to the public address, so the IdP posts back there.

## LDAP / Active Directory

Add an **LDAP / Active Directory** provider with:

- **Server address**: `ldaps://ldap.example.com:636`, or `ldap://…:389` on a trusted network.
- **Bind DN** and **bind password** of a read-only service account that can search people.
- **Search base**: where people live, such as `ou=people,dc=example,dc=com`.
- **User filter**: how to find one person from what they type, with `{0}` in its place. The default,
  `(|(uid={0})(sAMAccountName={0})(mail={0}))`, accepts a login name or an email on both OpenLDAP and
  Active Directory. What people type is escaped, so it cannot change the filter.
- **Attribute mapping**: defaults to `email=mail, name=displayName`.

The sign-in page shows a button for the provider that opens a username and password form. Sprint Boot finds
the person with the service account, then checks the password by binding as them. Accounts are identified
by `objectGUID` (Active Directory) or `entryUUID` (OpenLDAP), so a renamed person keeps their account.

**Test connection** binds with the service account. On Linux the host needs the OpenLDAP client library
(`libldap`), which the .NET LDAP client loads at runtime.

## Development

`docker compose -f deploy/docker/compose.yml --profile sso up -d` starts:

- Keycloak at `http://localhost:8180` (admin / admin) with the realm `sprint-boot`. It has an OIDC client
  `sprint-boot` and a SAML client for a provider named `keycloak-saml`; sign in as `alice` / `Sso123!dev`.
  The SAML metadata URL is `http://localhost:8180/realms/sprint-boot/protocol/saml/descriptor`.
- OpenLDAP at `ldap://localhost:1389`, bind DN `cn=admin,dc=sso,dc=local` / `admin`, search base
  `ou=people,dc=sso,dc=local`, with the person `lena` / `Ldap123!dev`.
