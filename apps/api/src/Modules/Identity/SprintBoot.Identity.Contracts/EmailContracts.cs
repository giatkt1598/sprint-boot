using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Services;

namespace SprintBoot.Identity;

public class EmailSettingsDto
{
    public required string Host { get; set; }

    public required int Port { get; set; }

    public required bool EnableSsl { get; set; }

    public required string UserName { get; set; }

    /// <summary>The password itself never leaves the server.</summary>
    public required bool HasPassword { get; set; }

    public required string FromAddress { get; set; }

    public required string FromName { get; set; }
}

public class SaveEmailSettingsInput
{
    [Required]
    [StringLength(256)]
    public required string Host { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; }

    public bool EnableSsl { get; set; }

    [StringLength(256)]
    public string? UserName { get; set; }

    /// <summary>Blank keeps the saved password, unless the user name is blank too.</summary>
    [StringLength(256)]
    public string? Password { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public required string FromAddress { get; set; }

    [StringLength(128)]
    public string? FromName { get; set; }
}

public class SendTestEmailInput
{
    [Required]
    [EmailAddress]
    public required string To { get; set; }
}

public static class EmailErrors
{
    public const string TestFailed = "SprintBoot:EmailTestFailed";
}

public interface IEmailSettingsAppService : IApplicationService
{
    Task<EmailSettingsDto> GetAsync();

    Task<EmailSettingsDto> UpdateAsync(SaveEmailSettingsInput input);

    Task SendTestAsync(SendTestEmailInput input);
}
