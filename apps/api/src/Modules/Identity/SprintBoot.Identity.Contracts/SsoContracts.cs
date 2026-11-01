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

    public bool IsEnabled { get; set; }
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
    [Required]
    [StringLength(512)]
    public required string DiscoveryUrl { get; set; }
}

public class ConnectionTestDto
{
    public required bool Succeeded { get; set; }

    public required string? Issuer { get; set; }
}

public class SignInProviderDto
{
    public required string Name { get; set; }

    public required string DisplayName { get; set; }
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

    Task<ConnectionTestDto> TestConnectionAsync(TestConnectionInput input);

    Task<AuthPolicyDto> GetPolicyAsync();

    Task<AuthPolicyDto> UpdatePolicyAsync(SaveAuthPolicyInput input);
}

public static class SsoErrors
{
    public const string ProviderNameTaken = "SprintBoot:ProviderNameTaken";
    public const string LastMethod = "SprintBoot:LastSignInMethod";
    public const string RegistrationClosed = "SprintBoot:RegistrationClosed";
    public const string DomainNotAllowed = "SprintBoot:DomainNotAllowed";
    public const string EmailNotVerified = "SprintBoot:EmailNotVerified";
    public const string EmailMissing = "SprintBoot:ExternalEmailMissing";
    public const string AccountDisabled = "SprintBoot:AccountDisabled";
    public const string ExternalFailed = "SprintBoot:ExternalSignInFailed";
    public const string LocalDisabled = "SprintBoot:LocalLoginDisabled";
    public const string DiscoveryRequired = "SprintBoot:DiscoveryUrlRequired";
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
