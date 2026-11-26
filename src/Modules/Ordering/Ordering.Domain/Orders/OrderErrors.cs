using Shop.SharedKernel;

namespace Ordering.Domain.Orders;

public static class OrderErrors
{
    public static Error InvalidLines(int maxLines, int maxQuantity) =>
        Error.Validation("ordering.invalid_lines",
            $"An order needs 1 to {maxLines} different items, each with a quantity of 1 to {maxQuantity}.");

    public static Error InvalidAddress(string message) =>
        Error.Validation("ordering.invalid_address", message);

    public static Error InvalidTransition(OrderStatus from, OrderStatus to) =>
        Error.Conflict("ordering.invalid_transition", $"An order that is {from} cannot become {to}.");

    public static Error EmptyBasket() =>
        Error.Conflict("ordering.empty_basket", "Your cart is empty.");

    /// <summary>Some line is unavailable (unknown, not Active) or short on stock.</summary>
    public static Error BasketHasIssues() =>
        Error.Conflict("ordering.basket_has_issues", "Some items in your cart are unavailable or short on stock.");

    /// <summary>The total the client showed differs from the live total.</summary>
    public static Error PriceChanged() =>
        Error.Conflict("ordering.price_changed", "Prices in your cart have changed. Please review it before paying.");

    public static Error MissingIdempotencyKey() =>
        Error.Validation("ordering.missing_idempotency_key", "The Idempotency-Key header must hold a GUID.");

    /// <summary>Also answered for another customer's order, so ids cannot be probed.</summary>
    public static Error OrderNotFound() =>
        Error.NotFound("ordering.order_not_found", "Order not found.");
}
