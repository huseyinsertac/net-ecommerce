namespace Payments.Application.Abstractions;

/// <summary>How payment sessions are opened; built by Infrastructure from <c>Payments:Timeout</c> and <c>App:PublicBaseUrl</c>.</summary>
/// <param name="Timeout">How long a shopper has to pay before the payment expires.</param>
/// <param name="PublicBaseUrl">The site's public origin, without a trailing slash; return URLs are built from it.</param>
/// <param name="ProcessingAlertAfter">How long past its deadline a payment may stay Processing at the gateway before it is logged as a warning.</param>
public sealed record PaymentSessionSettings(TimeSpan Timeout, string PublicBaseUrl, TimeSpan ProcessingAlertAfter);
