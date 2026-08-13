namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// String sentinels for the delivery states the provider itself never assigns. Provider-owned
/// statuses (queued, accepted, sending, sent, delivered, undelivered, failed, scheduled, canceled)
/// are stored verbatim as the lower-case strings the provider returns.
/// </summary>
public static class NotificationStatus
{
    /// <summary>Created locally, not yet handed to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The send attempt threw before the provider issued an identifier.</summary>
    public const string SendError = "send_error";

    /// <summary>Provider status for a scheduled message that has not gone out.</summary>
    public const string Scheduled = "scheduled";

    /// <summary>Provider status for a scheduled message that was called off before sending.</summary>
    public const string Canceled = "canceled";

    /// <summary>Terminal provider statuses — no further transitions are expected.</summary>
    public static bool IsTerminal(string status) =>
        status is "delivered" or "undelivered" or "failed" or Canceled or SendError;
}
