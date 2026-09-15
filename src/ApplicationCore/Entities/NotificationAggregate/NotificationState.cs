namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// eShop's own view of where a notification got to. This complements — it does not replace —
/// the provider's delivery outcome (<see cref="Notification.ProviderStatus"/>): this app can only
/// learn the provider's outcome by asking, since there is no public URL for the provider to call back.
/// </summary>
public enum NotificationState
{
    /// <summary>Created but no send has been attempted yet.</summary>
    Pending = 0,
    /// <summary>Handed to the provider for immediate delivery.</summary>
    Sent = 1,
    /// <summary>Accepted by the provider for a future send (the delivery-feedback follow-up).</summary>
    Scheduled = 2,
    /// <summary>The provider rejected the send, or the attempt threw. The order operation still succeeded.</summary>
    Failed = 3,
    /// <summary>A scheduled message that was called off before the provider sent it.</summary>
    Cancelled = 4
}
