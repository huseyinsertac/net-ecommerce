namespace Payments.Domain.Payments;

/// <summary><c>Pending → Succeeded | Failed | Expired</c>; the last three are final.</summary>
public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Expired,
}
