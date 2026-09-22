namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The local orchestration state of a notification. The provider's own delivery outcome is
/// carried separately in <see cref="SmsNotification.ProviderStatus"/> and refreshed from the
/// provider; this enum records what THIS application knows happened to the send attempt.
/// </summary>
public enum NotificationState
{
    /// <summary>Row created; the provider has not (yet) accepted the message.</summary>
    Pending = 0,

    /// <summary>The provider accepted the message; delivery detail lives in ProviderStatus.</summary>
    Sent = 1,

    /// <summary>The provider explicitly rejected the request (an API error). It was not sent.</summary>
    Failed = 2,

    /// <summary>
    /// Transport failed after the request may already have reached the provider — the outcome is
    /// unknown and must be settled by re-reading provider state, never assumed to be a failure.
    /// </summary>
    Unknown = 3,

    /// <summary>A scheduled message (delivery follow-up) was accepted for future sending.</summary>
    Scheduled = 4,

    /// <summary>A scheduled message was cancelled before it went out.</summary>
    Cancelled = 5
}
