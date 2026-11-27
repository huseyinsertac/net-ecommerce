using Catalog.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Payments.Contracts.Events;
using Shop.Application.Abstractions.Events;

namespace Catalog.Application.Reservations;

internal sealed class CommitStockOnPaymentSucceeded(
    IStockReservationRepository repository, TimeProvider clock, ILogger<CommitStockOnPaymentSucceeded> logger)
    : IIntegrationEventHandler<PaymentSucceeded>
{
    public Task Handle(PaymentSucceeded e, CancellationToken ct) =>
        ReservationClosing.Close(repository, e.OrderId, clock, logger, commit: true, ct);
}
