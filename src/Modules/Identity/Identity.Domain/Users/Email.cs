using Shop.SharedKernel;

namespace Identity.Domain.Users;

public sealed record Email
{
    private const int MaxLength = 254;

    private Email(string value)
    {
        Value = value;
        Normalized = value.ToUpperInvariant();
    }

    public string Value { get; }
    public string Normalized { get; }

    public static Result<Email> Create(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return IdentityErrors.InvalidEmail();
        }

        var at = value.IndexOf('@');
        if (at <= 0 || at == value.Length - 1 || value.IndexOf('@', at + 1) >= 0)
        {
            return IdentityErrors.InvalidEmail();
        }

        return new Email(value);
    }
}
