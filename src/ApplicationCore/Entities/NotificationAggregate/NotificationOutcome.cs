namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// eShop's coarse view of where a notification got to. The provider's own fine-grained status
/// (queued/sent/delivered/failed/undelivered/scheduled/canceled) is kept verbatim alongside this
/// in <see cref="SmsNotification.ProviderStatus"/>; this enum is what application logic branches on.
/// </summary>
public enum NotificationOutcome
{
    /// <summary>Local record created; the provider call has not completed yet.</summary>
    Pending = 0,
    /// <summary>Handed to the provider for immediate delivery.</summary>
    Sent = 1,
    /// <summary>Accepted by the provider and scheduled for a future send.</summary>
    Scheduled = 2,
    /// <summary>A scheduled message that was called off before it went out.</summary>
    Canceled = 3,
    /// <summary>The provider rejected the send, or it could not be handed over at all.</summary>
    Failed = 4,
    /// <summary>The shopper had no number on file, so nothing was sent.</summary>
    NotSent = 5
}
