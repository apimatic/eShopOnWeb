namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Coarse local view of where a notification got to, derived from the provider's own delivery status.
/// The exact provider wire status is kept alongside on <see cref="OrderNotification.ProviderStatus"/>.
/// </summary>
public enum NotificationSendState
{
    /// <summary>Created, not yet handed to the provider.</summary>
    Pending = 0,

    /// <summary>Provider accepted it (queued/sending/sent/accepted/scheduled) but final delivery is not yet known.</summary>
    Accepted = 1,

    /// <summary>Provider reports the handset received it.</summary>
    Delivered = 2,

    /// <summary>Provider reports it failed or was undelivered (an expected outcome for an unreachable destination).</summary>
    Failed = 3,

    /// <summary>A scheduled message that was called off before it went out.</summary>
    Canceled = 4,

    /// <summary>Send attempt outcome is indeterminate — it may have reached the provider once. Settle via reconciliation.</summary>
    Unknown = 5
}
