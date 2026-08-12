namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery status values a notification can carry. Most mirror the Twilio message
/// <c>status</c> field verbatim so the stored state stays faithful to the provider's own record.
/// <see cref="NotSent"/> is the one local-only value: it means the app never managed to hand the
/// message to the provider at all (so there is no provider identifier to reconcile or act on).
/// </summary>
public static class DeliveryStatuses
{
    // Local-only: the provider was never reached.
    public const string NotSent = "not_sent";

    // Provider (Twilio) statuses.
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>
    /// True when the status represents a message the provider actually has a send record for
    /// (i.e. it was handed off), as opposed to one that never left the app or is only scheduled.
    /// </summary>
    public static bool IsProviderSendRecord(string? status) =>
        status is not (null or NotSent or Scheduled or Canceled);

    /// <summary>
    /// True when the message did not reach the shopper and is therefore a candidate for resend.
    /// </summary>
    public static bool IsUndeliveredOutcome(string? status) =>
        status is Undelivered or Failed or NotSent;
}
