using Shop.SharedKernel;

namespace Identity.Domain.Users;

public static class IdentityErrors
{
    public static Error InvalidEmail() =>
        Error.Validation("identity.invalid_email", "Email must be 1-254 characters and look like name@host.");

    public static Error InvalidDisplayName() =>
        Error.Validation("identity.invalid_display_name", "Display name must be 1-80 characters.");

    public static Error EmailTaken() =>
        Error.Conflict("identity.email_taken", "An account with this email already exists.");

    public static Error InvalidCredentials() =>
        Error.Unauthorized("identity.invalid_credentials", "Email or password is incorrect.");

    public static Error InvalidRefreshToken() =>
        Error.Unauthorized("identity.invalid_refresh_token", "The refresh token is invalid or expired.");

    public static Error WeakPassword() =>
        Error.Validation("identity.weak_password",
            "Password must be at least 8 characters and contain a letter and a digit.");
}
