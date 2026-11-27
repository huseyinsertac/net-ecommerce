using Shop.SharedKernel;

namespace Payments.Contracts.Events;

public sealed record PaymentFailed(Guid OrderId, Guid PaymentId, string Reason) : IntegrationEvent;
