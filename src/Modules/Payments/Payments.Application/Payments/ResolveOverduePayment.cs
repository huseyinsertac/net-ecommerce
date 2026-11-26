using Microsoft.Extensions.Logging;
using Payments.Application.Abstractions;
using Payments.Application.Gateways;
using Payments.Domain.Payments;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Payments.Application.Payments;

/// <summary>
/// Ends a Pending payment whose time is up. The gateway is asked first, because a payment can succeed just before its
/// session closes and the notification may not have arrived: Paid → Succeeded; Open → the session is closed at the
/// gateway, then Expired; Expired → Expired.
/// </summary>
public sealed record ResolveOverduePayment(Guid PaymentId) : ICommand;

internal sealed class ResolveOverduePaymentHandler(
    IPaymentRepository payments,
    IPaymentGateway gateway,
    TimeProvider clock,
    ILogger<ResolveOverduePaymentHandler> logger) : ICommandHandler<ResolveOverduePayment>
{
    public async Task<Result> Handle(ResolveOverduePayment command, CancellationToken ct)
    {
        var payment = await payments.GetByIdAsync(command.PaymentId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (payment is null || payment.Status != PaymentStatus.Pending || payment.ExpiresAtUtc > now)
        {
            return Result.Success(); // ended (or extended) since the scan picked it
        }

        Result applied;
        if (payment.Provider != gateway.Name)
        {
            // Opened by a provider that is no longer configured: it cannot be asked, so the payment simply runs out.
            logger.LogWarning(
                "Payment {PaymentId} was opened with {Provider} but {Configured} is configured; expiring it without asking.",
                payment.Id, payment.Provider, gateway.Name);
            applied = payment.Expire(now);
        }
        else
        {
            var state = await gateway.GetSessionStateAsync(payment.ProviderSessionId, ct);
            switch (state)
            {
                case GatewaySessionState.Paid:
                    applied = payment.Succeed(now);
                    break;
                case GatewaySessionState.Open:
                    await gateway.ExpireSessionAsync(payment.ProviderSessionId, ct);
                    applied = payment.Expire(now);
                    break;
                case GatewaySessionState.Expired:
                    applied = payment.Expire(now);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown gateway session state {state}.");
            }
        }

        if (applied.IsFailure)
        {
            return applied;
        }

        await payments.SaveChangesAsync(ct);
        logger.LogInformation("Overdue payment {PaymentId} for order {OrderId} is now {Status}.", payment.Id, payment.OrderId, payment.Status);
        return Result.Success();
    }
}
