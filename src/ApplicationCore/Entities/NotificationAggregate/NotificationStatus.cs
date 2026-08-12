namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery outcome of a notification. The values mirror the provider's own message-status
/// vocabulary so that a stored notification reflects the state the provider owns, plus a local
/// <see cref="NotSent"/> sentinel for the case where nothing was ever handed to the provider
/// (for example, the shopper had no number on file).
/// </summary>
public static class NotificationStatus
{
    /// <summary>Nothing was sent to the provider (no number on file, or the send itself failed).</summary>
    public const string NotSent = "not_sent";

    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>
    /// Statuses from which no further delivery transition is expected, so there is no point
    /// re-fetching them from the provider.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or NotSent => true,
        _ => false
    };

    /// <summary>
    /// A message that did not reach the shopper and is therefore eligible to be re-sent.
    /// </summary>
    public static bool IsUndeliverable(string? status) => status switch
    {
        Undelivered or Failed => true,
        _ => false
    };
}
