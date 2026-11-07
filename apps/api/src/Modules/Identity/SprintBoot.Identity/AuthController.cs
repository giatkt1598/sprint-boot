using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.AntiForgery;
using Volo.Abp.Content;
using Volo.Abp.Uow;

namespace SprintBoot.Identity;

/// <summary>
/// Login uses the ASP.NET Core CSRF filter: ABP filters skip requests without an auth
/// cookie, which would leave login CSRF open.
/// </summary>
[Route("api/v1/auth")]
public class AuthController(IAuthAppService authAppService, IAbpAntiForgeryManager antiForgeryManager) : AbpController
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public void GetCsrf() => antiForgeryManager.SetCookie();

    [HttpPost("login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [UnitOfWork(IsDisabled = true)]
    public async Task LoginAsync([FromBody] LoginInput input)
    {
        switch (await authAppService.LoginAsync(input))
        {
            case LoginOutcome.LockedOut:
                throw new UserFriendlyException("Account is locked", AuthErrors.LockedOut);
            case LoginOutcome.LocalDisabled:
                throw new UserFriendlyException("Sign in with single sign-on", AuthErrors.LocalDisabled);
            case LoginOutcome.InvalidCredentials:
                throw new UserFriendlyException("Invalid email or password", AuthErrors.InvalidCredentials);
        }
    }

    [HttpPost("ldap-login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task LdapLoginAsync([FromBody] LdapLoginInput input)
    {
        if (await authAppService.LdapLoginAsync(input) != LoginOutcome.Succeeded)
        {
            throw new UserFriendlyException("Invalid username or password", AuthErrors.InvalidCredentials);
        }
    }

    [HttpGet("methods")]
    [AllowAnonymous]
    public Task<SignInMethodsDto> GetMethodsAsync() => authAppService.GetMethodsAsync();

    [HttpPost("invitation")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public Task<InvitationPreviewDto> GetInvitationAsync([FromBody] TokenInput input) => authAppService.GetInvitationAsync(input);

    [HttpPost("accept-invitation")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public Task AcceptInvitationAsync([FromBody] AcceptInvitationInput input) => authAppService.AcceptInvitationAsync(input);

    [HttpGet("me")]
    [Authorize]
    public Task<CurrentUserDto> GetMeAsync() => authAppService.GetMeAsync();

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public Task ForgotPasswordAsync([FromBody] ForgotPasswordInput input) => authAppService.ForgotPasswordAsync(input);

    [HttpPost("validate-reset-token")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public Task<ResetTokenStatusDto> ValidateResetTokenAsync([FromBody] ValidateResetTokenInput input) =>
        authAppService.ValidateResetTokenAsync(input);

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public Task ResetPasswordAsync([FromBody] ResetPasswordInput input) => authAppService.ResetPasswordAsync(input);

    [HttpPost("change-password")]
    [Authorize]
    public Task ChangePasswordAsync([FromBody] ChangePasswordInput input) => authAppService.ChangePasswordAsync(input);

    [HttpPut("profile")]
    [Authorize]
    public Task<CurrentUserDto> UpdateProfileAsync([FromBody] UpdateProfileInput input) =>
        authAppService.UpdateProfileAsync(input);

    [HttpPut("home-page")]
    [Authorize]
    public Task<CurrentUserDto> UpdateHomePageAsync([FromBody] HomePageDto input) =>
        authAppService.UpdateHomePageAsync(input);

    [HttpPost("avatar")]
    [Authorize]
    public Task<CurrentUserDto> UploadAvatarAsync(IRemoteStreamContent file) => authAppService.UploadAvatarAsync(file);

    [HttpDelete("avatar")]
    [Authorize]
    public Task<CurrentUserDto> DeleteAvatarAsync() => authAppService.DeleteAvatarAsync();

    [HttpPut("preferences")]
    [Authorize]
    public Task<CurrentUserDto> UpdatePreferencesAsync([FromBody] PreferencesDto input) =>
        authAppService.UpdatePreferencesAsync(input);

    [HttpPost("background")]
    [Authorize]
    public Task<CurrentUserDto> UploadBackgroundAsync(IRemoteStreamContent file) => authAppService.UploadBackgroundAsync(file);

    [HttpDelete("background")]
    [Authorize]
    public Task<CurrentUserDto> DeleteBackgroundAsync() => authAppService.DeleteBackgroundAsync();

    [HttpGet("logins")]
    [Authorize]
    public Task<List<LinkedLoginDto>> GetLoginsAsync() => authAppService.GetLoginsAsync();

    [HttpPost("logins/{provider}/link")]
    [Authorize]
    public Task<LinkStartDto> StartLinkAsync(string provider) => authAppService.StartLinkAsync(provider);

    [HttpDelete("logins/{provider}")]
    [Authorize]
    public Task UnlinkAsync(string provider) => authAppService.UnlinkAsync(provider);
}
