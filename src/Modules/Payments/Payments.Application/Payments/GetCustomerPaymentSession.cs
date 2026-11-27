using Payments.Application.Abstractions;
using Payments.Domain.Payments;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Payments.Application.Payments;

/// <summary>
/// The gateway session id of the order's payment, when the payment belongs to <paramref name="CustomerId"/>. Another
/// customer's payment answers <c>payments.payment_not_found</c>, exactly like a missing one.
/// </summary>
public sealed record GetCustomerPaymentSession(Guid CustomerId, Guid OrderId) : IQuery<string>;

internal sealed class GetCustomerPaymentSessionHandler(IPaymentRepository payments)
    : IQueryHandler<GetCustomerPaymentSession, string>
{
    public async Task<Result<string>> Handle(GetCustomerPaymentSession query, CancellationToken ct)
    {
        var payment = await payments.GetByOrderAsync(query.OrderId, ct);
        return payment is not null && payment.CustomerId == query.CustomerId
            ? payment.ProviderSessionId
            : PaymentErrors.PaymentNotFound();
    }
}
