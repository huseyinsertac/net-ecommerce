using Shop.SharedKernel;

namespace Payments.Domain.Payments;

public sealed record PaymentExpiredDomainEvent(Payment Payment) : DomainEvent;
