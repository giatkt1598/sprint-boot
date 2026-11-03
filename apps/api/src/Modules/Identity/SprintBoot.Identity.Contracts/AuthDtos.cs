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

public class CurrentUserDto
{
    public required Guid Id { get; set; }

    public required string Email { get; set; }

    public required string Name { get; set; }

    public required string Language { get; set; }

    public required string[] Permissions { get; set; }

    /// <summary>Null until the user uploads a picture.</summary>
    public string? AvatarUrl { get; set; }
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
}

public class ResetTokenStatusDto
{
    public required bool IsValid { get; set; }
}
