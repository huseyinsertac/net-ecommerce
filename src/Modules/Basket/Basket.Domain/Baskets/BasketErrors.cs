using Shop.SharedKernel;

namespace Basket.Domain.Baskets;

public static class BasketErrors
{
    public static Error InvalidQuantity(int max) =>
        Error.Validation("basket.invalid_quantity", $"Quantity must be between 0 and {max}.");

    public static Error TooManyLines(int max) =>
        Error.Conflict("basket.too_many_lines", $"A basket can hold at most {max} different items.");

    /// <summary>Unknown variant, or its product is not Active.</summary>
    public static Error VariantUnavailable() =>
        Error.NotFound("basket.variant_unavailable", "This item is not available.");

    /// <summary>The title is shown to shoppers verbatim.</summary>
    public static Error InsufficientStock(int available) =>
        Error.Conflict("basket.insufficient_stock", available <= 0 ? "Out of stock." : $"Only {available} left.");

    /// <summary>The shared demo admin is signed in by many visitors at once, so its basket is read-only. Shown verbatim.</summary>
    public static Error DemoAdminReadOnly() =>
        Error.Forbidden("basket.demo_admin_readonly",
            "The shared demo admin can't use the cart. Try the demo customer instead.");

    public static Error ConcurrentUpdate() =>
        Error.Conflict("basket.concurrent_update", "The basket was changed by another request. Please try again.");
}
