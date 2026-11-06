using Volo.Abp.Content;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EventBus;

namespace SprintBoot.Identity;

public enum ExternalProviderType
{
    Oidc = 0,
    Google = 1,
    GitHub = 2,

    /// <summary>DiscoveryUrl holds the IdP metadata address and ClientId this service's entity ID.</summary>
    Saml2 = 3,

    /// <summary>
    /// LDAP or Active Directory, signed into with a username and password on our page. DiscoveryUrl holds the
    /// server (ldap:// or ldaps://), ClientId the bind DN of a service account and the secret its password.
    /// </summary>
    Ldap = 4,
}

public class ExternalProviderDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    public required ExternalProviderType Type { get; set; }

    public required string? DiscoveryUrl { get; set; }

    public required string ClientId { get; set; }

    /// <summary>Secrets never leave the server; this only says one is stored.</summary>
    public required bool HasSecret { get; set; }

    public required string? Scopes { get; set; }

    public required string? EmailClaim { get; set; }

    public required string? NameClaim { get; set; }

    public required bool IsEnabled { get; set; }

    /// <summary>OIDC providers only: the picture on their sign-in button.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>OIDC providers only: the button color, as #RRGGBB.</summary>
    public string? ButtonColor { get; set; }

    /// <summary>LDAP only: where people are looked up, such as dc=example,dc=com.</summary>
    public string? SearchBase { get; set; }

    /// <summary>LDAP only: the filter that finds one person, with {0} for what they typed.</summary>
    public string? UserFilter { get; set; }

    /// <summary>
    /// SAML and LDAP only: their email counts as verified, so it links to an existing account with that address.
    /// Off, only people who linked the provider from their profile sign in with it.
    /// </summary>
    public bool TrustEmail { get; set; }
}

public class SaveProviderInput
{
    [Required]
    [RegularExpression("^[a-z0-9-]{2,32}$")]
    public required string Name { get; set; }

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string DisplayName { get; set; }

    public ExternalProviderType Type { get; set; }

    [StringLength(512)]
    public string? DiscoveryUrl { get; set; }

    [Required]
    [StringLength(256)]
    public required string ClientId { get; set; }

    /// <summary>Blank keeps the stored secret.</summary>
    public string? ClientSecret { get; set; }

    [StringLength(256)]
    public string? Scopes { get; set; }

    [StringLength(64)]
    public string? EmailClaim { get; set; }

    [StringLength(64)]
    public string? NameClaim { get; set; }

    /// <summary>OIDC only; blank keeps our own button style.</summary>
    [RegularExpression("^#[0-9A-Fa-f]{6}$")]
    public string? ButtonColor { get; set; }

    [StringLength(256)]
    public string? SearchBase { get; set; }

    [StringLength(256)]
    public string? UserFilter { get; set; }

    /// <summary>SAML and LDAP only; left out, the email is trusted.</summary>
    public bool? TrustEmail { get; set; }

    public bool IsEnabled { get; set; }
}

public class LdapLoginInput
{
    [Required]
    public required string Provider { get; set; }

    [Required]
    [StringLength(256)]
    public required string UserName { get; set; }

    [Required]
    public required string Password { get; set; }

    /// <summary>The invitation token when signing in from an invitation link.</summary>
    public string? Invitation { get; set; }
}

public class AuthPolicyDto
{
    public required bool LocalLoginEnabled { get; set; }

    public required bool SelfRegistrationEnabled { get; set; }

    /// <summary>Comma separated; empty allows every domain.</summary>
    public required string? AllowedDomains { get; set; }
}

public class SaveAuthPolicyInput
{
    public bool LocalLoginEnabled { get; set; }

    public bool SelfRegistrationEnabled { get; set; }

    [StringLength(512)]
    public string? AllowedDomains { get; set; }
}

public class TestConnectionInput
{
    public ExternalProviderType Type { get; set; }

    /// <summary>The OIDC issuer, the SAML metadata address or the LDAP server.</summary>
    [Required]
    [StringLength(512)]
    public required string DiscoveryUrl { get; set; }

    /// <summary>LDAP only: the bind DN to try.</summary>
    public string? ClientId { get; set; }

    /// <summary>LDAP only: the bind password; blank uses the one stored for <see cref="ProviderId"/>.</summary>
    public string? ClientSecret { get; set; }

    public Guid? ProviderId { get; set; }
}

public class ConnectionTestDto
{
    public required bool Succeeded { get; set; }

    /// <summary>The OIDC issuer or SAML entity ID found; for LDAP, the server that accepted the bind.</summary>
    public required string? Issuer { get; set; }
}

public class SignInProviderDto
{
    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Lets the sign-in page draw Google and GitHub in their own brand style.</summary>
    public required ExternalProviderType Type { get; set; }

    public string? LogoUrl { get; set; }

    public string? ButtonColor { get; set; }
}

public class SignInMethodsDto
{
    public required bool LocalLoginEnabled { get; set; }

    public required List<SignInProviderDto> Providers { get; set; }
}

public interface ISsoAdminAppService : IApplicationService
{
    Task<List<ExternalProviderDto>> GetProvidersAsync();

    Task<ExternalProviderDto> CreateProviderAsync(SaveProviderInput input);

    Task<ExternalProviderDto> UpdateProviderAsync(Guid id, SaveProviderInput input);

    Task DeleteProviderAsync(Guid id);

    /// <summary>PNG, JPEG, WebP or GIF up to 256 KB; only for OIDC providers.</summary>
    Task<ExternalProviderDto> UploadLogoAsync(Guid id, IRemoteStreamContent file);

    Task<ExternalProviderDto> DeleteLogoAsync(Guid id);

    Task<ConnectionTestDto> TestConnectionAsync(TestConnectionInput input);

    Task<AuthPolicyDto> GetPolicyAsync();

    Task<AuthPolicyDto> UpdatePolicyAsync(SaveAuthPolicyInput input);
}

public static class SsoErrors
{
    public const string ProviderNameTaken = "SprintBoot:ProviderNameTaken";
    public const string LastMethod = "SprintBoot:LastSignInMethod";
    public const string LogoInvalid = "SprintBoot:ProviderLogoInvalid";
    public const string AlreadyLinked = "SprintBoot:LoginAlreadyLinked";
    public const string LinkedElsewhere = "SprintBoot:LoginLinkedElsewhere";
    public const string RegistrationClosed = "SprintBoot:RegistrationClosed";
    public const string DomainNotAllowed = "SprintBoot:DomainNotAllowed";
    public const string EmailNotVerified = "SprintBoot:EmailNotVerified";
    public const string EmailMissing = "SprintBoot:ExternalEmailMissing";
    public const string AccountDisabled = "SprintBoot:AccountDisabled";
    public const string ExternalFailed = "SprintBoot:ExternalSignInFailed";
    public const string LocalDisabled = "SprintBoot:LocalLoginDisabled";
    public const string DiscoveryRequired = "SprintBoot:DiscoveryUrlRequired";
    public const string LdapAddressInvalid = "SprintBoot:LdapAddressInvalid";
    public const string LdapSearchBaseRequired = "SprintBoot:LdapSearchBaseRequired";
    public const string LdapFilterInvalid = "SprintBoot:LdapFilterInvalid";
}

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3,
}

public enum InvitationEmailStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
}

public class InvitationProjectInput
{
    public required Guid ProjectId { get; set; }

    [Required]
    public required string Role { get; set; }
}

public class CreateInvitationInput
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    /// <summary>System role; only people who manage users may set it.</summary>
    public string? SystemRole { get; set; }

    public List<InvitationProjectInput> Projects { get; set; } = [];
}

public class UpdateInvitationRoleInput
{
    public required Guid ProjectId { get; set; }

    [Required]
    public required string Role { get; set; }
}

public class InvitationDto
{
    public required Guid Id { get; set; }

    public required string Email { get; set; }

    public required InvitationStatus Status { get; set; }

    public required InvitationEmailStatus EmailStatus { get; set; }

    public required string? SystemRole { get; set; }

    public required UserRefDto? InvitedBy { get; set; }

    public required DateTime ExpiresAt { get; set; }

    public required DateTime CreationTime { get; set; }

    public required List<InvitationProjectDto> Projects { get; set; }
}

public class InvitationProjectDto
{
    public required Guid ProjectId { get; set; }

    public required string ProjectName { get; set; }

    public required string Role { get; set; }
}

public class InvitationLinkDto
{
    public required InvitationDto? Invitation { get; set; }

    /// <summary>Shown once, because only a hash is stored. Null when the person was added directly.</summary>
    public required string? Link { get; set; }

    public required bool AddedDirectly { get; set; }
}

public class InvitationListInput : PagedResultRequestDto
{
    public InvitationStatus? Status { get; set; }
}

public class InvitationPreviewDto
{
    public required string Email { get; set; }

    public required string? InvitedBy { get; set; }

    public required string? SystemRole { get; set; }

    public required List<InvitationProjectDto> Projects { get; set; }

    public required bool LocalLoginEnabled { get; set; }

    public required List<SignInProviderDto> Providers { get; set; }
}

public class TokenInput
{
    [Required]
    public required string Token { get; set; }
}

public class AcceptInvitationInput
{
    [Required]
    public required string Token { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; set; }

    [Required]
    public required string Password { get; set; }
}

public interface IInvitationAppService : IApplicationService
{
    Task<PagedResultDto<InvitationDto>> GetListAsync(InvitationListInput input);

    /// <summary>Every invitation into one space, newest first, for the admins of that space.</summary>
    Task<List<InvitationDto>> GetForProjectAsync(Guid projectId);

    /// <summary>Changes the role an invitation gives in one space, up to the caller's own.</summary>
    Task<InvitationDto> UpdateProjectRoleAsync(Guid id, UpdateInvitationRoleInput input);

    /// <summary>
    /// Takes one space out of an invitation; with nothing left to grant, the invitation is revoked.
    /// Space admins use this instead of revoking access to places they do not manage.
    /// </summary>
    Task RemoveProjectAsync(Guid id, Guid projectId);

    Task<InvitationLinkDto> CreateAsync(CreateInvitationInput input);

    Task<InvitationLinkDto> ResendAsync(Guid id);

    /// <summary>
    /// Only a hash is stored, so copying a link replaces it. The link sent by email stops working.
    /// </summary>
    Task<InvitationLinkDto> CreateLinkAsync(Guid id);

    Task RevokeAsync(Guid id);
}

public static class InvitationErrors
{
    public const string Invalid = "SprintBoot:InvitationInvalid";
    public const string EmailMismatch = "SprintBoot:InvitationEmailMismatch";
    public const string Forbidden = "SprintBoot:InvitationForbidden";
    public const string RoleTooHigh = "SprintBoot:InvitationRoleTooHigh";
    public const string RateLimited = "SprintBoot:InvitationRateLimited";
    public const string AlreadyRegistered = "SprintBoot:InvitationEmailRegistered";
    public const string NothingToGrant = "SprintBoot:InvitationNeedsRole";
}

/// <summary>The token travels encrypted, so the outbox table never holds a usable link.</summary>
[EventName("SprintBoot.Identity.InvitationEmailRequested")]
public class InvitationEmailEto
{
    public Guid InvitationId { get; set; }

    public string EncryptedToken { get; set; } = string.Empty;
}
