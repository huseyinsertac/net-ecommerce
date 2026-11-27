using Payments.Contracts.Events;
using Payments.Domain.Payments;
using Shop.Application.Abstractions.Events;

namespace Payments.Application.Payments;

/// <summary>Runs inside the payment's SaveChanges, so the outbox row commits with the status change.</summary>
internal sealed class PublishPaymentSucceeded(IEventPublisher publisher) : IDomainEventHandler<PaymentSucceededDomainEvent>
{
    public Task Handle(PaymentSucceededDomainEvent e, CancellationToken ct)
    {
        publisher.Publish(new PaymentSucceeded(e.Payment.OrderId, e.Payment.Id));
        return Task.CompletedTask;
    }
}
