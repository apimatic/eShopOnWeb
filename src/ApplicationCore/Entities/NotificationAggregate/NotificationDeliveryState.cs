using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known delivery states for a notification. The provider-owned states use the provider's own
/// wire values verbatim so the two line up during reconciliation; the two <c>local_*</c> states
/// describe outcomes the provider never saw (the send never reached it).
/// </summary>
public static class NotificationDeliveryState
{
    // Provider-owned wire values (Twilio message status values).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Receiving = "receiving";
    public const string Received = "received";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Read = "read";
    public const string PartiallyDelivered = "partially_delivered";

    // Local-only states: the send never reached the provider, so it owns no record of it.
    public const string SendFailed = "local_send_failed";

    /// <summary>
    /// A state that will not change again on its own, so there is no value in re-querying the provider.
    /// </summary>
    public static bool IsTerminal(string? state) => state switch
    {
        Delivered or Undelivered or Failed or Canceled or Read or Received or PartiallyDelivered or SendFailed => true,
        _ => false
    };

    /// <summary>Whether the shopper is considered to have actually received the message.</summary>
    public static bool IsDelivered(string? state) =>
        string.Equals(state, Delivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, Received, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, Read, StringComparison.OrdinalIgnoreCase);
}
