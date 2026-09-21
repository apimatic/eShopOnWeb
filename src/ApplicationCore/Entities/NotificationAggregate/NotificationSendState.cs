namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// This application's own view of a notification's lifecycle, distinct from the provider's delivery
/// status (which is stored verbatim on <see cref="Notification.ProviderStatus"/>).
/// </summary>
public enum NotificationSendState
{
    /// <summary>Local record created; the provider has not yet been called (or the call is in flight).</summary>
    Pending = 0,

    /// <summary>The provider accepted the message (queued/scheduled/sent). Delivery outcome lives in ProviderStatus.</summary>
    Sent = 1,

    /// <summary>The provider refused the request outright — no message was created.</summary>
    Failed = 2,

    /// <summary>The send could not be confirmed (transport failed after the request may have been received). Reconcile.</summary>
    Unknown = 3,

    /// <summary>A scheduled message that was called off before it went out.</summary>
    Canceled = 4
}
