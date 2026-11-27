using Microsoft.Extensions.Logging;
using Payments.Application.Abstractions;
using Payments.Domain.Payments;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Payments.Application.Payments;

/// <summary>
/// Ends the payment behind a gateway session. Always succeeds: an unknown session, or a payment that has already ended,
/// is logged and left alone, so a gateway that redelivers or reorders its notifications is never told to retry. One case is
/// louder: money taken at the gateway for a payment that already Expired or Failed is logged at Error,
/// because the shopper was charged for an order that will not ship and needs a refund.
/// </summary>
/// <param name="Reason">The failure reason; used only with <see cref="GatewayOutcome.Failed"/>.</param>
public sealed record ApplyGatewayOutcome(string SessionId, GatewayOutcome Outcome, string? Reason) : ICommand;

internal sealed class ApplyGatewayOutcomeHandler(
    IPaymentRepository payments,
    TimeProvider clock,
    ILogger<ApplyGatewayOutcomeHandler> logger) : ICommandHandler<ApplyGatewayOutcome>
{
    public const string DefaultFailureReason = "failed";

    public async Task<Result> Handle(ApplyGatewayOutcome command, CancellationToken ct)
    {
        var payment = await payments.GetBySessionIdAsync(command.SessionId, ct);
        if (payment is null)
        {
            logger.LogWarning("No payment for gateway session {SessionId}; ignoring {Outcome}.", command.SessionId, command.Outcome);
            return Result.Success();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var applied = command.Outcome switch
        {
            GatewayOutcome.Succeeded => payment.Succeed(now),
            GatewayOutcome.Failed => payment.Fail(
                string.IsNullOrWhiteSpace(command.Reason) ? DefaultFailureReason : command.Reason, now),
            GatewayOutcome.Expired => payment.Expire(now),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Outcome, "Unknown gateway outcome."),
        };

        if (applied.IsFailure)
        {
            if (command.Outcome == GatewayOutcome.Succeeded && payment.Status != PaymentStatus.Succeeded)
            {
                // Money taken for a payment that already ended: no PaymentSucceeded follows, so this log is the only signal.
                logger.LogError(
                    "Payment {PaymentId} was paid at the gateway but payment is {Status}; order {OrderId} needs a refund (session {SessionId}).",
                    payment.Id, payment.Status, payment.OrderId, command.SessionId);
                return Result.Success();
            }

            logger.LogInformation(
                "Payment {PaymentId} is already {Status}; ignoring {Outcome}.", payment.Id, payment.Status, command.Outcome);
            return Result.Success();
        }

        await payments.SaveChangesAsync(ct);
        return Result.Success();
    }
}
