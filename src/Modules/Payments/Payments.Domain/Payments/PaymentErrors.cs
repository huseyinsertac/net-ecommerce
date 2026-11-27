using Shop.SharedKernel;

namespace Payments.Domain.Payments;

public static class PaymentErrors
{
    public static Error InvalidTransition(PaymentStatus from, PaymentStatus to) =>
        Error.Conflict("payments.invalid_transition", $"A payment that is {from} cannot become {to}.");

    /// <summary>Also answered for another customer's payment, so order ids cannot be probed.</summary>
    public static Error PaymentNotFound() =>
        Error.NotFound("payments.payment_not_found", "Payment not found.");
}
