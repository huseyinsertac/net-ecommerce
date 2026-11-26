using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Identity.Application.Auth;

public sealed record Refresh(string? RefreshTokenRaw) : ICommand<AuthResult>;

internal sealed class RefreshHandler(
    IUserRepository users,
    ITokenIssuer tokens,
    IRefreshTokenFactory refreshFactory,
    IRefreshTokenPolicy refreshPolicy,
    TimeProvider clock) : ICommandHandler<Refresh, AuthResult>
{
    public async Task<Result<AuthResult>> Handle(Refresh command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshTokenRaw))
        {
            return IdentityErrors.InvalidRefreshToken();
        }

        var presentedHash = refreshFactory.Hash(command.RefreshTokenRaw);
        var user = await users.GetByRefreshTokenHashAsync(presentedHash, ct);
        if (user is null)
        {
            return IdentityErrors.InvalidRefreshToken();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var (raw, hash) = refreshFactory.Create();
        var rotated = user.Rotate(presentedHash, hash, now, refreshPolicy.Lifetime);

        // Saved on failure too: replaying a rotated token revokes its whole family, and that must persist.
        await users.SaveChangesAsync(ct);

        return rotated.IsFailure ? rotated.Error : AuthSession.Build(user, tokens, raw, rotated.Value, now);
    }
}
