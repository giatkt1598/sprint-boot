using Volo.Abp.Application.Services;

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
}
