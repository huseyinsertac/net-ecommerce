using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Payments.Contracts.Payments;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Ordering.Application.Orders;

/// <summary>The checkout status of one of the customer's orders. Another customer's order is reported as not found.</summary>
public sealed record GetCheckoutStatus(Guid CustomerId, Guid OrderId) : IQuery<CheckoutStatusDto>;

internal sealed class GetCheckoutStatusHandler(IOrderRepository orders, IPaymentCheckoutQuery payments)
    : IQueryHandler<GetCheckoutStatus, CheckoutStatusDto>
{
    private const string PendingPayment = "Pending";

    public async Task<Result<CheckoutStatusDto>> Handle(GetCheckoutStatus query, CancellationToken ct)
    {
        var order = await orders.GetForCustomerAsync(query.CustomerId, query.OrderId, ct);
        if (order is null)
        {
            return OrderErrors.OrderNotFound();
        }

        switch (order.Status)
        {
            case OrderStatus.Cancelled when order.CancellationReason == CancellationReason.OutOfStock:
                return new CheckoutStatusDto(CheckoutState.OutOfStock);
            case OrderStatus.Cancelled:
                return new CheckoutStatusDto(CheckoutState.Failed, Reason: order.CancellationReason?.ToString());
            case OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered:
                return new CheckoutStatusDto(CheckoutState.Paid);
        }

        // Placed. A payment that has already ended, but whose outcome Ordering has not applied yet, still reads Pending:
        // the client keeps polling until the order itself moves.
        var payment = await payments.GetByOrderAsync(order.Id, ct);
        return payment is { Status: PendingPayment, CheckoutUrl: { } url }
            ? new CheckoutStatusDto(CheckoutState.ReadyToPay, url, ExpiresAtUtc: payment.ExpiresAtUtc)
            : new CheckoutStatusDto(CheckoutState.Pending);
    }
}
