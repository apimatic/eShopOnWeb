namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Delivery outcomes for a notification. The values mirror the provider's own message
/// status vocabulary verbatim (so a later request can act on and report the state the
/// provider owns), plus one local sentinel for a message that was never handed to the
/// provider at all.
/// </summary>
public static class NotificationStatuses
{
    // Local sentinel: nothing was ever accepted by the provider (send threw, or the
    // account rejected the create request). Distinct from provider "failed"/"undelivered",
    // which mean the provider accepted the message and delivery later failed.
    public const string NotSent = "not_sent";

    // Provider statuses (verbatim).
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>True once the provider will not change the status any further.</summary>
    public static bool IsTerminal(string status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled => true,
        _ => false
    };

    /// <summary>True when the message did not reach the shopper and a resend is warranted.</summary>
    public static bool DidNotReachRecipient(string status) => status switch
    {
        Undelivered or Failed or NotSent => true,
        _ => false
    };
}
