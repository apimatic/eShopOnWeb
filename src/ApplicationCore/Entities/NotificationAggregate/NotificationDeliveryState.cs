namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The state of an <see cref="OrderNotification"/> from eShop's point of view. This is distinct from
/// the provider's own delivery outcome (captured in <see cref="OrderNotification.ProviderStatus"/>):
/// once a message has been accepted by the provider it moves to <see cref="Sent"/> and its live
/// delivery outcome is whatever the provider reports for its SID.
/// </summary>
public enum NotificationDeliveryState
{
    /// <summary>Created but not yet handed to the provider.</summary>
    Pending = 0,

    /// <summary>No contact number was on file for the shopper, so nothing was sent.</summary>
    NotAttempted = 1,

    /// <summary>The provider accepted the message and returned an identifier. Delivery outcome lives in <see cref="OrderNotification.ProviderStatus"/>.</summary>
    Sent = 2,

    /// <summary>The provider could not be asked to send the message (an error occurred before an identifier was obtained).</summary>
    FailedToSend = 3,

    /// <summary>A message that had been scheduled with the provider was cancelled before it went out.</summary>
    Cancelled = 4
}
