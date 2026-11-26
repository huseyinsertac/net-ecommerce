using Shop.SharedKernel;

namespace Ordering.Domain.Orders;

public sealed record ShippingAddress(
    string FullName, string Line1, string? Line2, string City, string PostalCode, string Country)
{
    public static Result<ShippingAddress> Create(
        string? fullName, string? line1, string? line2, string? city, string? postalCode, string? country)
    {
        var name = fullName?.Trim() ?? string.Empty;
        var l1 = line1?.Trim() ?? string.Empty;
        var l2 = string.IsNullOrWhiteSpace(line2) ? null : line2.Trim();
        var cty = city?.Trim() ?? string.Empty;
        var postal = postalCode?.Trim() ?? string.Empty;
        var ctry = country?.Trim().ToUpperInvariant() ?? string.Empty;

        if (name.Length is < 1 or > 100) return Invalid("Full name must be 1 to 100 characters.");
        if (l1.Length is < 1 or > 100) return Invalid("Address line 1 must be 1 to 100 characters.");
        if (l2 is { Length: > 100 }) return Invalid("Address line 2 must be at most 100 characters.");
        if (cty.Length is < 1 or > 100) return Invalid("City must be 1 to 100 characters.");
        if (postal.Length is < 1 or > 20) return Invalid("Postal code must be 1 to 20 characters.");
        if (ctry.Length != 2 || !ctry.All(c => c is >= 'A' and <= 'Z'))
            return Invalid("Country must be a 2-letter code.");

        return new ShippingAddress(name, l1, l2, cty, postal, ctry);
    }

    private static Error Invalid(string message) => OrderErrors.InvalidAddress(message);
}
