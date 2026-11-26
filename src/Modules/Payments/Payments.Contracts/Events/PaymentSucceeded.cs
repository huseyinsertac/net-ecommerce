using Shop.SharedKernel;

namespace Payments.Contracts.Events;

public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId) : IntegrationEvent;
