using Shop.SharedKernel;

namespace Payments.Contracts.Events;

public sealed record PaymentExpired(Guid OrderId, Guid PaymentId) : IntegrationEvent;
