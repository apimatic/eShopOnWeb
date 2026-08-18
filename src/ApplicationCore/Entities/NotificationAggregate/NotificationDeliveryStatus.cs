namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery-status values a notification can carry. These mirror the provider's own message
/// statuses one-for-one, plus a single application-only sentinel (<see cref="NotSent"/>) for the
/// case where the provider never accepted the message at all (so it owns no state for it).
/// </summary>
public static class NotificationDeliveryStatus
{
    /// <summary>The provider never accepted the message (create call failed); there is no provider record.</summary>
    public const string NotSent = "not_sent";

    // Provider-owned statuses (verbatim strings the messaging API returns).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";

    /// <summary>
    /// True when the status is a terminal delivery outcome — no further transition is expected,
    /// so it is safe to stop polling the provider for it.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled => true,
        _ => false
    };

    /// <summary>
    /// True when the message did not reach the shopper and an operator re-send is warranted.
    /// </summary>
    public static bool IsUndeliverable(string? status) => status switch
    {
        Undelivered or Failed or NotSent => true,
        _ => false
    };
}
