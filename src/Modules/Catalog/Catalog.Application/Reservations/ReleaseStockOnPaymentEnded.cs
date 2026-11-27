using Catalog.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Payments.Contracts.Events;
using Shop.Application.Abstractions.Events;

namespace Catalog.Application.Reservations;

/// <summary>Releases the reserved stock when the payment failed or expired.</summary>
internal sealed class ReleaseStockOnPaymentEnded(
    IStockReservationRepository repository, TimeProvider clock, ILogger<ReleaseStockOnPaymentEnded> logger)
    : IIntegrationEventHandler<PaymentFailed>, IIntegrationEventHandler<PaymentExpired>
{
    public Task Handle(PaymentFailed e, CancellationToken ct) => Release(e.OrderId, ct);

    public Task Handle(PaymentExpired e, CancellationToken ct) => Release(e.OrderId, ct);

    private Task Release(Guid orderId, CancellationToken ct) =>
        ReservationClosing.Close(repository, orderId, clock, logger, commit: false, ct);
}
