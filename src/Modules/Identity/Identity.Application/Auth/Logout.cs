using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Identity.Application.Auth;

public sealed record Logout(string? RefreshTokenRaw) : ICommand
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("RefreshTokenRaw = ").Append(RefreshTokenRaw is null ? "null" : "***");
        return true;
    }
}

internal sealed class LogoutHandler(IUserRepository users, IRefreshTokenFactory refreshFactory, TimeProvider clock)
    : ICommandHandler<Logout>
{
    private const int MaxAttempts = 3;

    /// <summary>Revokes the presented token's family. An absent or unknown token is already "logged out"; only a persistent concurrent-update conflict fails.</summary>
    public async Task<Result> Handle(Logout command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshTokenRaw))
        {
            return Result.Success();
        }

        var hash = refreshFactory.Hash(command.RefreshTokenRaw);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var user = await users.GetByRefreshTokenHashAsync(hash, ct);
                var token = user?.RefreshTokens.FirstOrDefault(t => t.TokenHash == hash);
                if (user is not null && token is not null)
                {
                    user.RevokeFamily(token.FamilyId, clock.GetUtcNow().UtcDateTime);
                    await users.SaveChangesAsync(ct);
                }

                return Result.Success();
            }
            catch (ConcurrencyConflictException)
            {
                if (attempt >= MaxAttempts)
                {
                    return Result.Failure(IdentityErrors.ConcurrentUpdate());
                }

                // A concurrent refresh may have added a token to this family; reload so the revoke covers it.
            }
        }
    }
}
