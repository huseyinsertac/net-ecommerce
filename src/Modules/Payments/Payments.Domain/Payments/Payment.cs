using Shop.SharedKernel;

namespace Payments.Domain.Payments;

/// <summary>
/// One payment attempt for an order, backed by a session at the payment provider. It starts Pending and ends exactly
/// once: Succeeded, Failed or Expired. Each ending raises a domain event that becomes the matching integration event.
/// </summary>
public sealed class Payment : AggregateRoot<Guid>
{
    private Payment() { }

    public Guid OrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public PaymentStatus Status { get; private set; }

    /// <summary>The gateway that holds the session (<c>Stripe</c> or <c>Fake</c>).</summary>
    public string Provider { get; private set; } = null!;

    public string ProviderSessionId { get; private set; } = null!;

    /// <summary>Where the shopper pays: the provider's hosted checkout page, or the simulated payment page.</summary>
    public string CheckoutUrl { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    public static Payment Start(
        Guid orderId, Guid customerId, Money amount, string provider, string sessionId, string url,
        DateTime nowUtc, DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(amount);

        return new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Status = PaymentStatus.Pending,
            Provider = provider,
            ProviderSessionId = sessionId,
            CheckoutUrl = url,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    public Result Succeed(DateTime nowUtc)
    {
        var result = Complete(PaymentStatus.Succeeded, nowUtc);
        if (result.IsSuccess) Raise(new PaymentSucceededDomainEvent(this));
        return result;
    }

    public Result Fail(string reason, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var result = Complete(PaymentStatus.Failed, nowUtc);
        if (result.IsFailure) return result;

        FailureReason = reason;
        Raise(new PaymentFailedDomainEvent(this));
        return result;
    }

    public Result Expire(DateTime nowUtc)
    {
        var result = Complete(PaymentStatus.Expired, nowUtc);
        if (result.IsSuccess) Raise(new PaymentExpiredDomainEvent(this));
        return result;
    }

    private Result Complete(PaymentStatus to, DateTime nowUtc)
    {
        if (Status != PaymentStatus.Pending)
        {
            return Result.Failure(PaymentErrors.InvalidTransition(Status, to));
        }

        Status = to;
        CompletedAtUtc = nowUtc;
        return Result.Success();
    }
}
