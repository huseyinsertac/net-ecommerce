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
}
