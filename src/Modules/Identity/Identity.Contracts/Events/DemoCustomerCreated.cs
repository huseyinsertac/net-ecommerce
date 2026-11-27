using Shop.SharedKernel;

namespace Identity.Contracts.Events;

public sealed record DemoCustomerCreated(Guid UserId, string Email) : IntegrationEvent;
