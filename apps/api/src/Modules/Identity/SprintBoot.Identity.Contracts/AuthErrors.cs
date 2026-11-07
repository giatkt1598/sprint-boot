namespace SprintBoot.Identity;

public static class AuthErrors
{
    public const string InvalidCredentials = "SprintBoot:InvalidCredentials";
    public const string LockedOut = "SprintBoot:LockedOut";
    public const string ResetLinkInvalid = "SprintBoot:ResetLinkInvalid";
    public const string CurrentPasswordInvalid = "SprintBoot:CurrentPasswordInvalid";
    public const string PasswordRejected = "SprintBoot:PasswordRejected";
    public const string LocalDisabled = SsoErrors.LocalDisabled;
    public const string CannotLockSelf = "SprintBoot:CannotLockSelf";
    public const string AvatarInvalid = "SprintBoot:AvatarInvalid";
    public const string CannotDeleteSelf = "SprintBoot:CannotDeleteSelf";
    public const string LastSystemAdmin = "SprintBoot:LastSystemAdmin";
    public const string InvalidHomePage = "SprintBoot:InvalidHomePage";
    public const string InvalidTimeZone = "SprintBoot:InvalidTimeZone";
}
