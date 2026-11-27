using Basket.Contracts;
using Catalog.Contracts.Variants;
using Ordering.Application.Abstractions;
using Ordering.Application.Orders;
using Ordering.Domain.Orders;
using Shop.Application.Abstractions;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Ordering.Application.Checkout;

/// <summary>
/// Places an order from the customer's basket at live catalog prices. Idempotent per (customer, key): a repeat, even a
/// concurrent one, returns the order the first request placed.
/// </summary>
/// <param name="ExpectedTotal">The total the shopper was shown; a different live total is refused rather than charged.</param>
public sealed record PlaceOrder(
    Guid CustomerId,
    string CustomerEmail,
    Guid IdempotencyKey,
    ShippingAddressDto? ShippingAddress,
    decimal ExpectedTotal) : ICommand<PlaceOrderResult>;

public sealed record PlaceOrderResult(Guid OrderId, string OrderNumber);

internal sealed class PlaceOrderHandler(
    IOrderRepository orders,
    IBasketCheckoutQuery basket,
    ICatalogVariantQuery catalog,
    TimeProvider clock) : ICommandHandler<PlaceOrder, PlaceOrderResult>
{
    /// <summary>Half a cent: anything closer is the same amount, rounded differently on the client.</summary>
    private const decimal TotalTolerance = 0.005m;

    public async Task<Result<PlaceOrderResult>> Handle(PlaceOrder command, CancellationToken ct)
    {
        if (await orders.GetByIdempotencyKeyAsync(command.CustomerId, command.IdempotencyKey, ct) is { } existing)
        {
            return Accepted(existing);
        }

        var input = command.ShippingAddress;
        var address = ShippingAddress.Create(
            input?.FullName, input?.Line1, input?.Line2, input?.City, input?.PostalCode, input?.Country);
        if (address.IsFailure)
        {
            return new ValidationError(new Dictionary<string, string[]> { ["shippingAddress"] = [address.Error.Message] });
        }

        var items = await basket.GetForUserAsync(command.CustomerId, ct);
        if (items.Count == 0)
        {
            return OrderErrors.EmptyBasket();
        }

        var snapshots = await catalog.GetAsync([.. items.Select(i => i.VariantId)], ct);
        var lines = new List<NewOrderLine>(items.Count);
        foreach (var item in items)
        {
            if (!snapshots.TryGetValue(item.VariantId, out var s) || !s.IsPurchasable || s.Available < item.Quantity)
            {
                return OrderErrors.BasketHasIssues();
            }

            lines.Add(new NewOrderLine(
                s.VariantId, s.ProductName, s.Sku, s.OptionsLabel, s.ThumbnailUrl, s.UnitPrice, item.Quantity));
        }

        var liveTotal = lines.Aggregate(Money.Zero, (sum, l) => sum + Money.Usd(l.UnitPrice) * l.Quantity).Amount;
        if (Math.Abs(liveTotal - command.ExpectedTotal) > TotalTolerance)
        {
            return OrderErrors.PriceChanged();
        }

        var number = await orders.NextOrderNumberAsync(ct);
        var placed = Order.Place(
            $"{Order.NumberPrefix}{number}", command.CustomerId, command.CustomerEmail, command.IdempotencyKey,
            address.Value, lines, clock.GetUtcNow().UtcDateTime);
        if (placed.IsFailure)
        {
            return placed.Error;
        }

        orders.Add(placed.Value);
        try
        {
            await orders.SaveChangesAsync(ct);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // A concurrent request with the same key won the unique index; answer with its order.
            var winner = await orders.GetByIdempotencyKeyAsync(command.CustomerId, command.IdempotencyKey, ct)
                ?? throw new InvalidOperationException("The idempotency key was taken but its order cannot be found.");
            return Accepted(winner);
        }

        return Accepted(placed.Value);
    }

    private static PlaceOrderResult Accepted(Order order) => new(order.Id, order.OrderNumber);
}
