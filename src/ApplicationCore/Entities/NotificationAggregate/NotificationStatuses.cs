namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Notification status values. Values mirroring the provider use its wire vocabulary
/// (queued, sent, delivered, ...); values only this application produces use a distinct prefix.
/// </summary>
public static class NotificationStatuses
{
    // Local outcomes (never produced by the provider)
    public const string SendFailed = "send-failed";

    // Provider wire values
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Canceled = "canceled";
    public const string Receiving = "receiving";
    public const string Received = "received";
    public const string Read = "read";
    public const string PartiallyDelivered = "partially_delivered";

    /// <summary>
    /// Terminal-failure and terminal-success states, per the integration contract.
    /// Any unrecognized (e.g. future provider) status is treated as non-terminal so it
    /// is never misreported as a final failure.
    /// </summary>
    public static bool IsTerminal(string? status) =>
        status is Failed or Undelivered or Canceled or Delivered or Read or SendFailed;
}
