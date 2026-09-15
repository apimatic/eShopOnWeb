namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery outcome values for a notification. The provider-owned values mirror Twilio's message
/// status strings verbatim (queued, sending, sent, delivered, undelivered, failed, scheduled,
/// canceled, ...); <see cref="SubmitFailed"/> is the only app-only value and marks a message the
/// application could not even hand to the provider (e.g. the API call itself threw).
/// </summary>
public static class NotificationStatus
{
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Accepted = "accepted";

    /// <summary>The application could not submit the message to the provider at all.</summary>
    public const string SubmitFailed = "submit_failed";

    /// <summary>Statuses from which no further transition is expected, so no need to re-poll the provider.</summary>
    public static bool IsTerminal(string? status) => status is Delivered or Undelivered or Failed or Canceled or SubmitFailed;
}
