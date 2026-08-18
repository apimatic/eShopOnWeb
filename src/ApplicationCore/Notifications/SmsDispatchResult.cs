namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// The provider's state for a message after it accepted a send/schedule/read/cancel/redact request.
/// A carrier-undeliverable message is still a successful dispatch here — the provider accepted it and
/// returns a <see cref="Status"/> of e.g. "undelivered"/"failed" with an error code. Only a transport or
/// configuration failure (which yields no provider state at all) is surfaced as an exception instead.
/// </summary>
public record SmsDispatchResult
{
    /// <summary>The provider's identifier for the message.</summary>
    public string? MessageSid { get; init; }

    /// <summary>The provider's current delivery outcome (wire value, e.g. "queued", "delivered", "undelivered").</summary>
    public string? Status { get; init; }

    public int? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>The message text as the provider currently holds it (empty once redacted).</summary>
    public string? Body { get; init; }
}
