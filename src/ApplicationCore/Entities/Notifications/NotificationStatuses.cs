namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// Well-known values for <see cref="OrderNotification.Status"/>.
///
/// A notification's status mostly mirrors the provider's own delivery status verbatim (its wire value, e.g.
/// <c>queued</c>, <c>sent</c>, <c>delivered</c>, <c>undelivered</c>, <c>failed</c>, <c>scheduled</c>,
/// <c>canceled</c>) so the record carries the state the provider owns. The two values below are ours, for
/// states the provider has no say in.
/// </summary>
public static class NotificationStatuses
{
    /// <summary>Created locally but not yet handed to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The provider never accepted the message (create call errored). Distinct from the provider's
    /// own <c>failed</c>, which means the carrier refused an accepted message.</summary>
    public const string SendFailed = "send_failed";

    /// <summary>Provider wire status for a message the carrier refused.</summary>
    public const string Failed = "failed";

    /// <summary>Provider wire status for a message accepted by the provider but not delivered by the carrier.</summary>
    public const string Undelivered = "undelivered";

    /// <summary>Provider wire status for a not-yet-sent scheduled message.</summary>
    public const string Scheduled = "scheduled";

    /// <summary>Provider wire status for a scheduled message that was called off before sending.</summary>
    public const string Canceled = "canceled";

    /// <summary>
    /// True when a message in this status did not reach the shopper and is therefore a candidate for resend.
    /// </summary>
    public static bool IsUndeliveredOutcome(string status) =>
        status == SendFailed || status == Failed || status == Undelivered;
}
