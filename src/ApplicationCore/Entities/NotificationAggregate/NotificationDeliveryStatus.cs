namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Normalized delivery outcome for a notification, derived from the provider's own message
/// status. The raw provider status string is retained separately on the notification.
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>We attempted to send but the provider call itself failed; no provider message exists.</summary>
    NotSent = 0,

    /// <summary>Accepted/queued by the provider, not yet handed to a carrier.</summary>
    Queued = 1,

    /// <summary>Scheduled with the provider for a future send time.</summary>
    Scheduled = 2,

    /// <summary>Handed to the carrier / in flight.</summary>
    Sending = 3,

    /// <summary>Sent to the carrier (delivery receipt not yet confirmed).</summary>
    Sent = 4,

    /// <summary>Confirmed delivered to the handset.</summary>
    Delivered = 5,

    /// <summary>The carrier accepted then refused the message (e.g. unreachable destination).</summary>
    Undelivered = 6,

    /// <summary>The provider failed to send the message.</summary>
    Failed = 7,

    /// <summary>A scheduled message that was called off before it went out.</summary>
    Canceled = 8,

    /// <summary>Provider reported a status we do not map explicitly.</summary>
    Unknown = 9
}
