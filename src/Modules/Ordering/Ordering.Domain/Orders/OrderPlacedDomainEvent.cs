using Shop.SharedKernel;

namespace Ordering.Domain.Orders;

public sealed record OrderPlacedDomainEvent(Order Order) : DomainEvent;
