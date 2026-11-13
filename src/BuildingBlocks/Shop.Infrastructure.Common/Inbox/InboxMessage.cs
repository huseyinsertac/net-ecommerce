namespace Shop.Infrastructure.Common.Inbox;

/// <summary>Records that <see cref="Consumer"/> has handled message <see cref="MessageId"/>. PK (MessageId, Consumer).</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public string Consumer { get; init; } = "";

    public DateTime ProcessedOnUtc { get; init; }
}
