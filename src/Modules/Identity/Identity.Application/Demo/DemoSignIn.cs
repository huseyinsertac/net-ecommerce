using Identity.Application.Abstractions;
using Identity.Application.Auth;
using Identity.Domain.Users;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Identity.Application.Demo;

/// <summary>Creates a throwaway demo customer and signs it in.</summary>
public sealed record StartDemoCustomer : ICommand<AuthResult>;

/// <summary>Signs in the seeded, shared demo admin.</summary>
public sealed record StartDemoAdmin : ICommand<AuthResult>;

internal sealed class StartDemoCustomerHandler(
    IUserRepository users,
    ITokenIssuer tokens,
    IRefreshTokenFactory refreshFactory,
    IRefreshTokenPolicy refreshPolicy,
    TimeProvider clock) : ICommandHandler<StartDemoCustomer, AuthResult>
{
    private const int MaxAttempts = 3;

    public async Task<Result<AuthResult>> Handle(StartDemoCustomer command, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var email = Email.Create(DemoAccounts.NewCustomerEmail());
            if (email.IsFailure)
            {
                return email.Error;
            }

            var user = User.CreateDemoCustomer(email.Value, now);
            var (raw, hash) = refreshFactory.Create();
            var refresh = user.IssueRefreshToken(hash, now, refreshPolicy.Lifetime);
            users.Add(user);
            try
            {
                await users.SaveChangesAsync(ct);
                return AuthSession.Build(user, tokens, raw, refresh, now);
            }
            catch (DuplicateEmailException)
            {
                // 32 random bits collided with an existing demo address; draw a new one.
            }
        }

        return IdentityErrors.ConcurrentUpdate();
    }
}

internal sealed class StartDemoAdminHandler(
    IUserRepository users,
    ITokenIssuer tokens,
    IRefreshTokenFactory refreshFactory,
    IRefreshTokenPolicy refreshPolicy,
    TimeProvider clock) : ICommandHandler<StartDemoAdmin, AuthResult>
{
    private const int MaxAttempts = 3;

    public async Task<Result<AuthResult>> Handle(StartDemoAdmin command, CancellationToken ct)
    {
        var email = Email.Create(DemoAccounts.AdminEmail).Value;
        var user = await users.GetByNormalizedEmailAsync(email.Normalized, ct);
        if (user is not { IsDemo: true })
        {
            return IdentityErrors.DemoUnavailable();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        for (var attempt = 1; ; attempt++)
        {
            var (raw, hash) = refreshFactory.Create();
            var refresh = user.IssueRefreshToken(hash, now, refreshPolicy.Lifetime);
            try
            {
                await users.SaveChangesAsync(ct);
                return AuthSession.Build(user, tokens, raw, refresh, now);
            }
            catch (ConcurrencyConflictException)
            {
                if (attempt >= MaxAttempts)
                {
                    return IdentityErrors.ConcurrentUpdate();
                }

                // Many visitors share this account, so parallel sign-ins are expected; reload and issue again.
                user = await users.GetByIdAsync(user.Id, ct);
                if (user is null)
                {
                    return IdentityErrors.DemoUnavailable();
                }
            }
        }
    }
}
