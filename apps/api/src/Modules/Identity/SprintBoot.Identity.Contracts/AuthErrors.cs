namespace SprintBoot.Identity;

public static class AuthErrors
{
    public const string InvalidCredentials = "SprintBoot:InvalidCredentials";
    public const string LockedOut = "SprintBoot:LockedOut";
    public const string ResetLinkInvalid = "SprintBoot:ResetLinkInvalid";
    public const string CurrentPasswordInvalid = "SprintBoot:CurrentPasswordInvalid";
    public const string PasswordRejected = "SprintBoot:PasswordRejected";
    public const string CannotLockSelf = "SprintBoot:CannotLockSelf";
}
