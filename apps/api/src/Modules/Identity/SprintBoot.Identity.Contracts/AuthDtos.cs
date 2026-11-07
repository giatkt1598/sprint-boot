using System.ComponentModel.DataAnnotations;

namespace SprintBoot.Identity;

public enum LoginOutcome
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
    LocalDisabled,
}

public class LoginInput
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Required]
    public required string Password { get; set; }
}

public class LinkedLoginDto
{
    /// <summary>The provider name used in sign-in addresses, such as google.</summary>
    public required string Provider { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Null for a link to a provider that has since been switched off.</summary>
    public required ExternalProviderType? Type { get; set; }

    public required bool IsLinked { get; set; }

    public required bool CanLink { get; set; }

    /// <summary>False when this is the only way left to sign in.</summary>
    public required bool CanUnlink { get; set; }

    /// <summary>The logo an admin uploaded for an OIDC provider.</summary>
    public string? LogoUrl { get; set; }
}

public class LinkStartDto
{
    /// <summary>Open as a full page: it leaves for the IdP and comes back to the profile.</summary>
    public required string Url { get; set; }
}

public class CurrentUserDto
{
    public required Guid Id { get; set; }

    public required string Email { get; set; }

    public required string Name { get; set; }

    public required string Language { get; set; }

    public required string[] Permissions { get; set; }

    /// <summary>Null until the user uploads a picture.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>The job title the person set; null when left empty.</summary>
    public string? Title { get; set; }

    /// <summary>An IANA time zone such as Asia/Ho_Chi_Minh; null means the browser's own.</summary>
    public string? TimeZone { get; set; }

    /// <summary>Where the app opens; clients fall back to the last space when it is no longer available.</summary>
    public required HomePageDto HomePage { get; set; }
}

public enum HomePageKind
{
    /// <summary>The space the person used last, as before anyone chooses.</summary>
    LastSpace = 0,
    Space = 1,
    Search = 2,
    Notifications = 3,
}

public enum SpaceView
{
    Backlog = 0,
    Board = 1,
    Issues = 2,
    Epics = 3,
}

public class HomePageDto
{
    public HomePageKind Kind { get; set; }

    /// <summary>Set only for <see cref="HomePageKind.Space"/>.</summary>
    public Guid? ProjectId { get; set; }

    public SpaceView? View { get; set; }
}

public class ForgotPasswordInput
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    public string Language { get; set; } = "en";
}

public class ResetPasswordInput
{
    [Required]
    public required Guid UserId { get; set; }

    [Required]
    public required string Token { get; set; }

    [Required]
    public required string NewPassword { get; set; }
}

public class ValidateResetTokenInput
{
    [Required]
    public required Guid UserId { get; set; }

    [Required]
    public required string Token { get; set; }
}

public class ChangePasswordInput
{
    [Required]
    public required string CurrentPassword { get; set; }

    [Required]
    public required string NewPassword { get; set; }
}

public class UpdateProfileInput
{
    [Required]
    [StringLength(128)]
    public required string Name { get; set; }

    [Required]
    [RegularExpression("^(en|vi)$")]
    public required string Language { get; set; }

    [StringLength(128)]
    public string? Title { get; set; }

    /// <summary>An IANA time zone id; empty or null goes back to the browser's.</summary>
    [StringLength(64)]
    public string? TimeZone { get; set; }
}

public class ResetTokenStatusDto
{
    public required bool IsValid { get; set; }
}
