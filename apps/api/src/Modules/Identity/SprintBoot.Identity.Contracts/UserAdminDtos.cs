using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace SprintBoot.Identity;

public class UserListInput : PagedResultRequestDto
{
    public string? Search { get; set; }
}

public class UserSummaryDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }

    /// <summary>Null until the user uploads a picture; clients then show initials.</summary>
    public string? AvatarUrl { get; set; }

    public required string SystemRole { get; set; }

    public required bool IsLocked { get; set; }

    public required DateTime CreationTime { get; set; }

    public required DateTime? LastSignInAt { get; set; }
}

public class SignInMethodDto
{
    public required string Method { get; set; }

    public required bool IsEnabled { get; set; }

    public required string Account { get; set; }

    public required DateTime? LinkedAt { get; set; }

    public required DateTime? LastUsedAt { get; set; }
}

public class CreateUserInput
{
    [Required]
    [StringLength(128)]
    public required string Name { get; set; }

    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Required]
    public required string Password { get; set; }

    [Required]
    public required string SystemRole { get; set; }
}

public class SetSystemRoleInput
{
    [Required]
    public required string SystemRole { get; set; }
}

public static class SystemRoles
{
    public const string Admin = "admin";
    public const string Member = "member";
}
