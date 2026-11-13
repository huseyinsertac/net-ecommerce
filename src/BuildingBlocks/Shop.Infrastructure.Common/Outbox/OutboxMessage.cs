namespace Shop.Infrastructure.Common.Outbox;

/// <summary>An integration event persisted in the same transaction as the state change that produced it.</summary>
public sealed class OutboxMessage
{
    /// <summary>Equal to the integration event's <c>Id</c>, so consumers can de-duplicate on it.</summary>
    public Guid Id { get; init; }

    /// <summary>The event CLR type's <c>FullName</c>; resolved through <see cref="Events.IntegrationEventTypeRegistry"/>.</summary>
    public string Type { get; init; } = "";

    /// <summary>The event serialized as JSON (stored as <c>jsonb</c>).</summary>
    public string Content { get; init; } = "";

    public DateTime OccurredOnUtc { get; init; }

    public DateTime? ProcessedOnUtc { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }
}

public sealed class OutboxOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public int BatchSize { get; set; } = 20;

    /// <summary>After this many failed attempts a message is parked (never picked up again).</summary>
    public int MaxAttempts { get; set; } = 5;
}
