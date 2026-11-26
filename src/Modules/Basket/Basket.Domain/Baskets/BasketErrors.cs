using Shop.SharedKernel;

namespace Basket.Domain.Baskets;

public static class BasketErrors
{
    public static Error InvalidQuantity(int max) =>
        Error.Validation("basket.invalid_quantity", $"Quantity must be between 0 and {max}.");

    public static Error TooManyLines(int max) =>
        Error.Conflict("basket.too_many_lines", $"A basket can hold at most {max} different items.");
}
