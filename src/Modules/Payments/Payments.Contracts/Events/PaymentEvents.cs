using Shop.SharedKernel;

namespace Payments.Contracts.Events;

public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId) : IntegrationEvent;

public sealed record PaymentFailed(Guid OrderId, Guid PaymentId, string Reason) : IntegrationEvent;

public sealed record PaymentExpired(Guid OrderId, Guid PaymentId) : IntegrationEvent;
