using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace SprintBoot.Identity;

public interface IAuthAppService : IApplicationService
{
    /// <summary>Starts a cookie session used only by the authorize endpoint.</summary>
    Task<LoginOutcome> LoginAsync(LoginInput input);

    Task<CurrentUserDto> GetMeAsync();

    Task<SignInMethodsDto> GetMethodsAsync();

    Task<InvitationPreviewDto> GetInvitationAsync(TokenInput input);

    Task AcceptInvitationAsync(AcceptInvitationInput input);

    /// <summary>Always succeeds so the response never reveals whether an email exists.</summary>
    Task ForgotPasswordAsync(ForgotPasswordInput input);

    Task<ResetTokenStatusDto> ValidateResetTokenAsync(ValidateResetTokenInput input);

    Task ResetPasswordAsync(ResetPasswordInput input);

    Task ChangePasswordAsync(ChangePasswordInput input);

    Task<CurrentUserDto> UpdateProfileAsync(UpdateProfileInput input);

    /// <summary>PNG, JPEG, WebP or GIF up to 2 MB; replaces the previous picture.</summary>
    Task<CurrentUserDto> UploadAvatarAsync(IRemoteStreamContent file);

    Task<CurrentUserDto> DeleteAvatarAsync();

    Task<List<LinkedLoginDto>> GetLoginsAsync();

    Task<LinkStartDto> StartLinkAsync(string provider);

    /// <summary>Refused when it is the only way left to sign in.</summary>
    Task UnlinkAsync(string provider);
}
