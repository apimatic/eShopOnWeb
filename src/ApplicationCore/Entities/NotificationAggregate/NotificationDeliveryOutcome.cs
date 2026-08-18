namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A coarse, provider-agnostic classification of where a message got to, derived from the
/// provider's own fine-grained status. The provider remains the source of truth; this is a
/// convenience view for callers.
/// </summary>
public enum NotificationDeliveryOutcome
{
    /// <summary>No message was sent (e.g. the shopper has no number on file).</summary>
    NotSent = 0,

    /// <summary>Accepted by the provider and still in flight (queued/sending/sent/accepted).</summary>
    InFlight = 1,

    /// <summary>The provider confirmed the message reached the handset (delivered/read).</summary>
    Reached = 2,

    /// <summary>The provider says the message did not reach the handset (undelivered/failed).</summary>
    NotReached = 3,

    /// <summary>Queued with the provider for future delivery (scheduled).</summary>
    Scheduled = 4,

    /// <summary>A scheduled message that was called off before it went out (canceled).</summary>
    Canceled = 5,

    /// <summary>The send request itself could not be handed to the provider (local/transport error).</summary>
    SendError = 6
}
