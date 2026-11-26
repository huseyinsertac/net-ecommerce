using Catalog.Application.Abstractions;
using Catalog.Contracts.Events;
using Catalog.Domain.Products;
using Catalog.Domain.Reservations;
using Microsoft.Extensions.Logging;
using Ordering.Contracts.Events;
using Payments.Contracts.Events;
using Shop.Application.Abstractions.Events;

namespace Catalog.Application.Reservations;

/// <summary>
/// Reserves stock for every line of a placed order, all or nothing. A <c>DbUpdateConcurrencyException</c> (another order
/// took the stock first) is not caught: it propagates so the outbox retries and the retry sees the new stock level.
/// The inbox wrapper performs the single save that persists the stock, the reservation and the published event.
/// </summary>
internal sealed class OrderPlacedHandler(
    IStockReservationRepository repository,
    IEventPublisher publisher,
    TimeProvider clock,
    ILogger<OrderPlacedHandler> logger) : IIntegrationEventHandler<OrderPlaced>
{
    public async Task Handle(OrderPlaced e, CancellationToken ct)
    {
        if (await repository.GetAsync(e.OrderId, ct) is not null)
        {
            logger.LogInformation("Stock for order {OrderId} is already reserved; ignoring duplicate OrderPlaced.", e.OrderId);
            return;
        }

        // The same variant may appear on several lines; reserve them together so availability is checked once.
        var wanted = e.Lines
            .GroupBy(l => l.VariantId)
            .Select(g => (VariantId: g.Key, Quantity: g.Sum(l => l.Quantity), AnyInvalid: g.Any(l => l.Quantity <= 0)))
            .ToList();

        var products = await repository.GetProductsByVariantIdsAsync([.. wanted.Select(w => w.VariantId)], ct);
        var lines = new List<ReservationLine>();
        var failedSkus = new List<string>();

        foreach (var (variantId, quantity, anyInvalid) in wanted)
        {
            var product = products.FirstOrDefault(p => p.Variants.Any(v => v.Id == variantId));
            if (product is null)
            {
                failedSkus.Add(variantId.ToString());
                continue;
            }

            var sku = product.Variants.First(v => v.Id == variantId).Sku;
            if (anyInvalid || product.Status != ProductStatus.Active || product.ReserveStock(variantId, quantity).IsFailure)
            {
                failedSkus.Add(sku);
                continue;
            }

            lines.Add(new ReservationLine(product.Id, variantId, quantity));
        }

        if (failedSkus.Count > 0 || lines.Count == 0)
        {
            repository.DiscardChanges(); // nothing may be reserved when any line fails
            publisher.Publish(new StockReservationFailed(e.OrderId, failedSkus));
            return;
        }

        repository.Add(StockReservation.Create(e.OrderId, lines, clock.GetUtcNow().UtcDateTime));
        publisher.Publish(new StockReserved(e.OrderId));
    }
}

internal sealed class PaymentSucceededHandler(
    IStockReservationRepository repository, TimeProvider clock, ILogger<PaymentSucceededHandler> logger)
    : IIntegrationEventHandler<PaymentSucceeded>
{
    public Task Handle(PaymentSucceeded e, CancellationToken ct) =>
        ReservationClosing.Close(repository, e.OrderId, clock, logger, commit: true, ct);
}

internal sealed class PaymentFailedHandler(
    IStockReservationRepository repository, TimeProvider clock, ILogger<PaymentFailedHandler> logger)
    : IIntegrationEventHandler<PaymentFailed>
{
    public Task Handle(PaymentFailed e, CancellationToken ct) =>
        ReservationClosing.Close(repository, e.OrderId, clock, logger, commit: false, ct);
}

internal sealed class PaymentExpiredHandler(
    IStockReservationRepository repository, TimeProvider clock, ILogger<PaymentExpiredHandler> logger)
    : IIntegrationEventHandler<PaymentExpired>
{
    public Task Handle(PaymentExpired e, CancellationToken ct) =>
        ReservationClosing.Close(repository, e.OrderId, clock, logger, commit: false, ct);
}

/// <summary>Moves a Reserved reservation to Committed or Released and applies the same change to the stock.</summary>
internal static class ReservationClosing
{
    public static async Task Close(
        IStockReservationRepository repository, Guid orderId, TimeProvider clock, ILogger logger, bool commit, CancellationToken ct)
    {
        var reservation = await repository.GetAsync(orderId, ct);
        if (reservation is null)
        {
            logger.LogInformation("No stock reservation for order {OrderId}; nothing to change.", orderId);
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (!(commit ? reservation.TryCommit(now) : reservation.TryRelease(now)))
        {
            logger.LogInformation(
                "Stock reservation for order {OrderId} is already {Status}; ignoring.", orderId, reservation.Status);
            return;
        }

        var products = await repository.GetProductsByVariantIdsAsync([.. reservation.Lines.Select(l => l.VariantId)], ct);
        foreach (var line in reservation.Lines)
        {
            var product = products.Single(p => p.Id == line.ProductId);
            var result = commit
                ? product.CommitStock(line.VariantId, line.Quantity)
                : product.ReleaseStock(line.VariantId, line.Quantity);
            if (result.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Stock reservation for order {orderId} is inconsistent with stock: {result.Error.Code}.");
            }
        }
    }
}
