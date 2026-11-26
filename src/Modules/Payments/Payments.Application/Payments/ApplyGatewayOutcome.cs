using Microsoft.Extensions.Logging;
using Payments.Application.Abstractions;
using Payments.Domain.Payments;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Payments.Application.Payments;

/// <summary>
/// Ends the payment behind a gateway session. Always succeeds: an unknown session, or a payment that has already ended,
/// is logged and left alone, so a gateway that redelivers or reorders its notifications is never told to retry.
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
            logger.LogInformation(
                "Payment {PaymentId} is already {Status}; ignoring {Outcome}.", payment.Id, payment.Status, command.Outcome);
            return Result.Success();
        }

        await payments.SaveChangesAsync(ct);
        return Result.Success();
    }
}
