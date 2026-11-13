namespace Shop.SharedKernel;

public sealed record Money
{
    public const string UsdCurrency = "USD";

    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency;
    }

    public static Money Usd(decimal amount) => new(amount, UsdCurrency);
    public static Money Zero => Usd(0m);

    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException(
                $"Cannot add amounts in different currencies ({left.Currency} and {right.Currency}).");
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator *(Money money, int quantity) =>
        new(money.Amount * quantity, money.Currency);
}
