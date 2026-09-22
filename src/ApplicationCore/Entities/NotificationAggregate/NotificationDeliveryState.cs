namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// eShop's interpretation of the provider's own delivery outcome for a message. The raw provider
/// status string is kept alongside this (see <see cref="OrderNotification.ProviderStatus"/>); this
/// enum is what the app branches on. An outcome that could not be read is <see cref="Pending"/>,
/// never assumed successful.
/// </summary>
public enum NotificationDeliveryState
{
    /// <summary>Created locally, not yet handed to the provider.</summary>
    Pending = 0,
    /// <summary>Accepted by the provider and queued/sending — a real interim state, not success.</summary>
    Queued = 1,
    /// <summary>Provider accepted a scheduled message; it has not gone out yet.</summary>
    Scheduled = 2,
    /// <summary>Handed to the carrier (Twilio <c>sent</c>).</summary>
    Sent = 3,
    /// <summary>Confirmed delivered to the handset.</summary>
    Delivered = 4,
    /// <summary>The carrier refused it (Twilio <c>undelivered</c>).</summary>
    Undelivered = 5,
    /// <summary>The send failed at the provider (Twilio <c>failed</c>).</summary>
    Failed = 6,
    /// <summary>A scheduled message that was called off before it sent.</summary>
    Canceled = 7,
    /// <summary>The provider call itself failed (transport error); no Sid was obtained.</summary>
    SendFailed = 8
}
