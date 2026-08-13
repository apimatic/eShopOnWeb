namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// eShop's normalized view of where a message got to. This is derived from — and kept alongside —
/// the provider's own raw status (see <see cref="Notification.ProviderStatus"/>), so a later
/// request can both act on and report on the message without re-deriving it.
/// </summary>
public enum NotificationStatus
{
    /// <summary>Created locally but not yet handed to the provider.</summary>
    Pending = 0,

    /// <summary>Accepted by the provider and scheduled for a future send (follow-up messages).</summary>
    Scheduled = 1,

    /// <summary>Handed to the provider and in flight (queued / accepted / sending / sent).</summary>
    Sending = 2,

    /// <summary>The provider confirmed delivery to the handset.</summary>
    Delivered = 3,

    /// <summary>The provider accepted it but the carrier reported it was not delivered.</summary>
    Undelivered = 4,

    /// <summary>The provider reported the send failed.</summary>
    Failed = 5,

    /// <summary>A scheduled message that was called off before it went out.</summary>
    Canceled = 6,

    /// <summary>The provider rejected the create call outright; no message was ever created.</summary>
    SendFailed = 7
}
