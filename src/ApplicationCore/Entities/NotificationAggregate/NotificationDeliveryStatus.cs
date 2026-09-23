namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The app's view of where a message got to, derived from the provider's own status. Grouped as:
/// done (<see cref="Sent"/>, <see cref="Delivered"/>), failed (<see cref="Failed"/>,
/// <see cref="Cancelled"/>) and not-yet (<see cref="Pending"/>, <see cref="Scheduled"/>,
/// <see cref="Unknown"/>).
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>Accepted/queued/sending — the provider has it but delivery is not settled.</summary>
    Pending = 0,
    /// <summary>Queued with the provider for a future send (a scheduled follow-up).</summary>
    Scheduled = 1,
    /// <summary>Handed to the carrier (Twilio <c>sent</c>/<c>read</c>).</summary>
    Sent = 2,
    /// <summary>Confirmed delivered.</summary>
    Delivered = 3,
    /// <summary>Did not reach the shopper (Twilio <c>failed</c>/<c>undelivered</c>) — eligible for resend.</summary>
    Failed = 4,
    /// <summary>A not-yet-sent message that was called off (Twilio <c>canceled</c>).</summary>
    Cancelled = 5,
    /// <summary>The send transport failed after the request may have been received; outcome not known.</summary>
    Unknown = 6
}
