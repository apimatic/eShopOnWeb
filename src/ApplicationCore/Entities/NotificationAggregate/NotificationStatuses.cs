namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known notification status values. Most mirror the provider's own wire statuses
/// (queued, sent, delivered, undelivered, failed, scheduled, canceled …); a couple are synthetic
/// values the shop uses when there is no provider status to record.
/// </summary>
public static class NotificationStatuses
{
    /// <summary>Created locally, not yet submitted to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The send could not be submitted to the provider at all.</summary>
    public const string SendFailed = "send_failed";

    /// <summary>A scheduled future send (mirrors the provider's "scheduled").</summary>
    public const string Scheduled = "scheduled";

    /// <summary>A scheduled send that was called off before it went out (mirrors the provider's "canceled").</summary>
    public const string Canceled = "canceled";

    /// <summary>Fallback when the provider returned no recognisable status.</summary>
    public const string Unknown = "unknown";

    // Provider delivery outcomes that indicate the message did not reach the shopper —
    // i.e. the states a resend is appropriate for.
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";

    /// <summary>True when a status indicates the message did not reach the shopper and a resend is warranted.</summary>
    public static bool IsUndeliverable(string? status) =>
        string.Equals(status, Undelivered, System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, SendFailed, System.StringComparison.OrdinalIgnoreCase);
}
