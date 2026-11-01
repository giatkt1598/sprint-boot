using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Emailing;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace SprintBoot.Identity;

/// <summary>Saved values override appsettings, and the mail sender reads them on every send.</summary>
[Authorize(SprintBootPermissions.ManageEmail)]
public class EmailSettingsAppService(ISettingManager settingManager, ISettingProvider settings, IEmailSender emailSender)
    : ApplicationService, IEmailSettingsAppService
{
    public async Task<EmailSettingsDto> GetAsync() => new()
    {
        Host = await settings.GetOrNullAsync(EmailSettingNames.Smtp.Host) ?? string.Empty,
        Port = await settings.GetAsync(EmailSettingNames.Smtp.Port, 25),
        EnableSsl = await settings.GetAsync(EmailSettingNames.Smtp.EnableSsl, false),
        UserName = await settings.GetOrNullAsync(EmailSettingNames.Smtp.UserName) ?? string.Empty,
        HasPassword = !string.IsNullOrEmpty(await settings.GetOrNullAsync(EmailSettingNames.Smtp.Password)),
        FromAddress = await settings.GetOrNullAsync(EmailSettingNames.DefaultFromAddress) ?? string.Empty,
        FromName = await settings.GetOrNullAsync(EmailSettingNames.DefaultFromDisplayName) ?? string.Empty,
    };

    public async Task<EmailSettingsDto> UpdateAsync(SaveEmailSettingsInput input)
    {
        var userName = input.UserName?.Trim() ?? string.Empty;
        await settingManager.SetGlobalAsync(EmailSettingNames.Smtp.Host, input.Host.Trim());
        await settingManager.SetGlobalAsync(EmailSettingNames.Smtp.Port, input.Port.ToString());
        await settingManager.SetGlobalAsync(EmailSettingNames.Smtp.EnableSsl, input.EnableSsl.ToString());
        await settingManager.SetGlobalAsync(EmailSettingNames.Smtp.UserName, userName);
        await settingManager.SetGlobalAsync(EmailSettingNames.Smtp.UseDefaultCredentials, (userName.Length == 0).ToString());
        await settingManager.SetGlobalAsync(EmailSettingNames.DefaultFromAddress, input.FromAddress.Trim());
        await settingManager.SetGlobalAsync(EmailSettingNames.DefaultFromDisplayName, input.FromName?.Trim() ?? string.Empty);

        if (userName.Length == 0)
        {
            await settingManager.SetGlobalAsync(EmailSettingNames.Smtp.Password, string.Empty);
        }
        else if (!string.IsNullOrEmpty(input.Password))
        {
            await settingManager.SetGlobalAsync(EmailSettingNames.Smtp.Password, input.Password);
        }

        return await GetAsync();
    }

    public async Task SendTestAsync(SendTestEmailInput input)
    {
        try
        {
            await emailSender.SendAsync(input.To, "Sprint Boot test email", "Email from Sprint Boot works.", isBodyHtml: false);
        }
        catch (Exception exception) when (exception is not BusinessException)
        {
            Logger.LogWarning(exception, "Test email failed");
            throw new BusinessException(EmailErrors.TestFailed);
        }
    }
}
