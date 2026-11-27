using Shop.SharedKernel;

namespace Identity.Domain.Users;

public sealed record DemoCustomerCreatedDomainEvent(User User) : DomainEvent;
