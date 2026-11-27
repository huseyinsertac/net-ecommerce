namespace Payments.Infrastructure.Webhooks;

/// <summary>A Stripe event id already handled; a redelivery of the same id is skipped.</summary>
public sealed class ProcessedWebhookEvent
{
    public string EventId { get; init; } = "";

    public DateTime ReceivedOnUtc { get; init; }
}
