using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(User user, string password);

    /// <summary>True when <paramref name="password"/> matches <paramref name="hash"/> (also when the hash uses older parameters).</summary>
    bool Verify(User user, string hash, string password);
}
