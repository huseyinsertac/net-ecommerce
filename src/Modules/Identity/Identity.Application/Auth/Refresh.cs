using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Identity.Application.Auth;

public sealed record Refresh(string? RefreshTokenRaw) : ICommand<AuthResult>
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("RefreshTokenRaw = ").Append(RefreshTokenRaw is null ? "null" : "***");
        return true;
    }
}

internal sealed class RefreshHandler(
    IUserRepository users,
    ITokenIssuer tokens,
    IRefreshTokenFactory refreshFactory,
    IRefreshTokenPolicy refreshPolicy,
    TimeProvider clock) : ICommandHandler<Refresh, AuthResult>
{
    private const int MaxAttempts = 3;

    public async Task<Result<AuthResult>> Handle(Refresh command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshTokenRaw))
        {
            return IdentityErrors.InvalidRefreshToken();
        }

        var presentedHash = refreshFactory.Hash(command.RefreshTokenRaw);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RotateAsync(presentedHash, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                // Every token mutation bumps User.SessionVersion, so a concurrent rotation, logout or theft revocation of
                // the same user makes our save conflict. The repository discarded its stale state; reload and decide again:
                // a just-rotated token is then handled by the single reuse grace, a revoked family fails.
            }
        }
    }

    private async Task<Result<AuthResult>> RotateAsync(string presentedHash, CancellationToken ct)
    {
        var user = await users.GetByRefreshTokenHashAsync(presentedHash, ct);
        if (user is null)
        {
            return IdentityErrors.InvalidRefreshToken();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var (raw, hash) = refreshFactory.Create();
        var rotated = user.Rotate(presentedHash, hash, now, refreshPolicy.Lifetime, refreshPolicy.ReuseGrace);

        // Saved on failure too: replaying a rotated token revokes its whole family, and that must persist.
        await users.SaveChangesAsync(ct);

        return rotated.IsFailure ? rotated.Error : AuthSession.Build(user, tokens, raw, rotated.Value, now);
    }
}
