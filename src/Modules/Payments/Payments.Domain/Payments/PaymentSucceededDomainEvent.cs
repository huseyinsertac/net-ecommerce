using Shop.SharedKernel;

namespace Payments.Domain.Payments;

public sealed record PaymentSucceededDomainEvent(Payment Payment) : DomainEvent;
