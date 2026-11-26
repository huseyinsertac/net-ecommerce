using Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure.Security;

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (salted, versioned format) behind the module's abstraction.</summary>
internal sealed class AspNetPasswordHasher : Application.Abstractions.IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(User user, string password) => _inner.HashPassword(user, password);

    // SuccessRehashNeeded still means the password matched; upgrading the stored hash is a separate concern.
    public bool Verify(User user, string hash, string password) =>
        _inner.VerifyHashedPassword(user, hash, password) is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
}
