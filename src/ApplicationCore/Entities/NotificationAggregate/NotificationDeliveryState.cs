namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// eShop's normalized view of what the provider reported for a message. The raw provider status string is
/// kept alongside this on the <see cref="OrderNotification"/>; this enum is the coarse outcome eShop acts on.
/// </summary>
public enum NotificationDeliveryState
{
    /// <summary>No provider call has completed yet.</summary>
    NotSent = 0,

    /// <summary>Accepted/queued/sending/scheduled — in flight, not a final outcome.</summary>
    Pending = 1,

    /// <summary>Left Twilio or confirmed delivered (sent/delivered/received/read).</summary>
    Delivered = 2,

    /// <summary>failed/undelivered — did not reach the shopper; eligible for resend.</summary>
    Failed = 3,

    /// <summary>A scheduled message that was cancelled before it went out.</summary>
    Cancelled = 4,

    /// <summary>A send whose transport failed after the request may have been received — outcome unknown.</summary>
    Unknown = 5
}
