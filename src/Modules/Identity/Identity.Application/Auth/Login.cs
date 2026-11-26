using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Identity.Application.Auth;

public sealed record Login(string Email, string Password) : ICommand<AuthResult>
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Email = ").Append(Email).Append(", Password = ***");
        return true;
    }
}

internal sealed class LoginValidator : AbstractValidator<Login>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}

internal sealed class LoginHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    ITokenIssuer tokens,
    IRefreshTokenFactory refreshFactory,
    IRefreshTokenPolicy refreshPolicy,
    TimeProvider clock) : ICommandHandler<Login, AuthResult>
{
    private const int MaxAttempts = 3;

    // A valid hash of a random password, computed once. Verifying against it costs the same as a real check, so unknown
    // emails, demo users and password-less users take as long to reject as a wrong password.
    private static string? _dummyHash;
    private static readonly User DummyUser =
        User.CreateDemoCustomer(Email.Create("dummy@dummy.invalid").Value, DateTime.UnixEpoch);

    public async Task<Result<AuthResult>> Handle(Login command, CancellationToken ct)
    {
        var email = Email.Create(command.Email);
        var user = email.IsSuccess ? await users.GetByNormalizedEmailAsync(email.Value.Normalized, ct) : null;

        var passwordOk = false;
        if (user is { CanLogInWithPassword: true })
        {
            passwordOk = hasher.Verify(user, user.PasswordHash!, command.Password);
        }
        else
        {
            _dummyHash ??= hasher.Hash(DummyUser, Convert.ToHexString(Guid.NewGuid().ToByteArray()) + "aA1");
            hasher.Verify(DummyUser, _dummyHash, command.Password);
        }

        if (!passwordOk || user is null)
        {
            return IdentityErrors.InvalidCredentials();
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

                // A parallel sign-in or refresh touched this user first; reload the fresh state and issue again.
                user = await users.GetByIdAsync(user.Id, ct);
                if (user is null)
                {
                    return IdentityErrors.InvalidCredentials();
                }
            }
        }
    }
}
