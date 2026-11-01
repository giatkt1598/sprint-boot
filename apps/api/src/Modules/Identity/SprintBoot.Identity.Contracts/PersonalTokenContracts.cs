using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Services;

namespace SprintBoot.Identity;

public enum PersonalTokenKind
{
    Api = 0,
    Mcp = 1,
}

public class PersonalTokenDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required PersonalTokenKind Kind { get; set; }

    public required bool ReadOnly { get; set; }

    /// <summary>The first characters only, enough to recognise a token.</summary>
    public required string Prefix { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required DateTime? ExpiresAt { get; set; }

    public required DateTime? LastUsedAt { get; set; }
}

public class CreatedPersonalTokenDto : PersonalTokenDto
{
    /// <summary>Shown this once; only a hash is kept.</summary>
    public required string Token { get; set; }
}

public class CreatePersonalTokenInput
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; set; }

    public PersonalTokenKind Kind { get; set; }

    public bool ReadOnly { get; set; }

    /// <summary>Blank never expires.</summary>
    [Range(1, 3650)]
    public int? ExpiresInDays { get; set; }
}

public interface IPersonalTokenAppService : IApplicationService
{
    Task<List<PersonalTokenDto>> GetListAsync();

    Task<CreatedPersonalTokenDto> CreateAsync(CreatePersonalTokenInput input);

    Task DeleteAsync(Guid id);
}

public static class PersonalTokenClaims
{
    public const string Kind = "sb_token_kind";
    public const string ReadOnly = "sb_token_read_only";
}
