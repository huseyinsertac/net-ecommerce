using Catalog.Contracts.Events;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Shop.Application.Abstractions.Events;

namespace Ordering.Application.Reactions;

/// <summary>Cancels the order as out of stock when Catalog could not reserve every line.</summary>
internal sealed class CancelOnStockReservationFailed(
    IOrderRepository orders, TimeProvider clock, ILogger<CancelOnStockReservationFailed> logger)
    : IIntegrationEventHandler<StockReservationFailed>
{
    public Task Handle(StockReservationFailed e, CancellationToken ct)
    {
        logger.LogInformation(
            "Stock could not be reserved for order {OrderId} ({Skus}).", e.OrderId, string.Join(", ", e.Skus));
        return OrderReaction.ApplyToPlacedAsync(
            orders, e.OrderId, nameof(StockReservationFailed),
            order => order.Cancel(CancellationReason.OutOfStock, clock.GetUtcNow().UtcDateTime), logger, ct);
    }
}
