using Catalog.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Catalog.Application.Reservations;

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
