using Shop.SharedKernel;

namespace Payments.Domain.Payments;

public sealed record PaymentFailedDomainEvent(Payment Payment) : DomainEvent;
