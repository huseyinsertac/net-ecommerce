using Payments.Contracts.Events;
using Payments.Domain.Payments;
using Shop.Application.Abstractions.Events;

namespace Payments.Application.Payments;

/// <summary>Runs inside the payment's SaveChanges, so the outbox row commits with the status change.</summary>
internal sealed class PublishPaymentFailed(IEventPublisher publisher) : IDomainEventHandler<PaymentFailedDomainEvent>
{
    public Task Handle(PaymentFailedDomainEvent e, CancellationToken ct)
    {
        publisher.Publish(new PaymentFailed(e.Payment.OrderId, e.Payment.Id, e.Payment.FailureReason!));
        return Task.CompletedTask;
    }
}
