using Identity.Application.Abstractions;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Identity.Application.Auth;

public sealed record Logout(string? RefreshTokenRaw) : ICommand;

internal sealed class LogoutHandler(IUserRepository users, IRefreshTokenFactory refreshFactory, TimeProvider clock)
    : ICommandHandler<Logout>
{
    /// <summary>Revokes the presented token's family. Always succeeds: an absent or unknown token is already "logged out".</summary>
    public async Task<Result> Handle(Logout command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshTokenRaw))
        {
            return Result.Success();
        }

        var hash = refreshFactory.Hash(command.RefreshTokenRaw);
        var user = await users.GetByRefreshTokenHashAsync(hash, ct);
        var token = user?.RefreshTokens.FirstOrDefault(t => t.TokenHash == hash);
        if (user is not null && token is not null)
        {
            user.RevokeFamily(token.FamilyId, clock.GetUtcNow().UtcDateTime);
            await users.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
