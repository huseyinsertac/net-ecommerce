using Shop.SharedKernel;

namespace Catalog.Contracts.Events;

/// <summary>Stock for every line of the order is now reserved.</summary>
public sealed record StockReserved(Guid OrderId) : IntegrationEvent;
