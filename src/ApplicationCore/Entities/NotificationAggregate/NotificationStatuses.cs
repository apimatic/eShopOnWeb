namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known delivery-outcome strings. The provider's own status values (queued, sending,
/// sent, delivered, undelivered, failed, scheduled, canceled, ...) are stored verbatim;
/// these constants cover the states this application assigns before or instead of a provider status.
/// </summary>
public static class NotificationStatuses
{
    /// <summary>Created locally, not yet submitted to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>Submission to the provider failed outright — no message was created there.</summary>
    public const string SubmissionFailed = "submission_failed";

    /// <summary>Provider returned no recognisable status.</summary>
    public const string Unknown = "unknown";

    // Mirrors of provider values this app references by name.
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Delivered = "delivered";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Undelivered = "undelivered";
}
