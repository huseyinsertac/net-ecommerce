using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Identity.Application.Auth;

public sealed record Register(string Email, string Password, string DisplayName) : ICommand<AuthResult>;

internal sealed class RegisterValidator : AbstractValidator<Register>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().Length(PasswordRules.MinLength, PasswordRules.MaxLength);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
    }
}

internal static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static bool IsStrongEnough(string password) =>
        password.Length >= MinLength && password.Any(char.IsLetter) && password.Any(char.IsDigit);
}

internal sealed class RegisterHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    ITokenIssuer tokens,
    IRefreshTokenFactory refreshFactory,
    IRefreshTokenPolicy refreshPolicy,
    TimeProvider clock) : ICommandHandler<Register, AuthResult>
{
    public async Task<Result<AuthResult>> Handle(Register command, CancellationToken ct)
    {
        var email = Email.Create(command.Email);
        if (email.IsFailure)
        {
            return email.Error;
        }

        if (!PasswordRules.IsStrongEnough(command.Password))
        {
            return IdentityErrors.WeakPassword();
        }

        if (await users.GetByNormalizedEmailAsync(email.Value.Normalized, ct) is not null)
        {
            return IdentityErrors.EmailTaken();
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // The hasher wants a user only as a context (PasswordHasher<T> ignores it), but the real user needs the hash at
        // construction, so a throwaway instance serves as the context.
        var hashContext = User.CreateDemoCustomer(email.Value, now);
        var user = User.Register(email.Value, command.DisplayName, hasher.Hash(hashContext, command.Password), now);
        if (user.IsFailure)
        {
            return user.Error;
        }

        var (raw, hash) = refreshFactory.Create();
        var refresh = user.Value.IssueRefreshToken(hash, now, refreshPolicy.Lifetime);

        users.Add(user.Value);
        await users.SaveChangesAsync(ct);

        return AuthSession.Build(user.Value, tokens, raw, refresh, now);
    }
}
