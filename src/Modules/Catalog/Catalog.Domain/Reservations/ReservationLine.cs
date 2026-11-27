namespace Catalog.Domain.Reservations;

public sealed record ReservationLine(Guid ProductId, Guid VariantId, int Quantity);
