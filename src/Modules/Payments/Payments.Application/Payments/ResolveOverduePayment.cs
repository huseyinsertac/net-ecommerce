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
/// gateway, then Expired; Expired → Expired; Processing (finished, money not landed) → left Pending until the gateway
/// reports the result (logged as a warning once it is long past its deadline).
/// </summary>
public sealed record ResolveOverduePayment(Guid PaymentId) : ICommand;

internal sealed class ResolveOverduePaymentHandler(
    IPaymentRepository payments,
    IPaymentGateway gateway,
    TimeProvider clock,
    PaymentSessionSettings settings,
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
                case GatewaySessionState.Processing:
                    // Finished by the shopper, money not landed yet: the session can no longer be closed, and the result
                    // arrives as a notification. Ask again on the next scan.
                    var age = now - payment.ExpiresAtUtc;
                    if (age > settings.ProcessingAlertAfter)
                    {
                        logger.LogWarning(
                            "Payment {PaymentId} for order {OrderId} still processing at the gateway {Age} after its deadline; leaving it Pending.",
                            payment.Id, payment.OrderId, age);
                    }
                    else
                    {
                        logger.LogInformation(
                            "Overdue payment {PaymentId} for order {OrderId} is still processing at the gateway; leaving it Pending.",
                            payment.Id, payment.OrderId);
                    }

                    return Result.Success();
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
