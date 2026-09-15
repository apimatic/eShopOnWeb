using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery-status values stored on a <see cref="Notification"/>. Most are the messaging
/// provider's own wire values; a few are application sentinels for states the provider never
/// assigns (a message that never reached the provider, or one not yet attempted).
/// </summary>
public static class DeliveryStatuses
{
    // Application sentinels
    public const string Pending = "pending";        // created, not yet handed to the provider
    public const string SendFailed = "send_failed"; // a real failure handing the message to the provider
    public const string Unknown = "unknown";        // provider accepted but returned no status

    // Provider (Twilio) wire values
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Accepted = "accepted";

    /// <summary>A message still queued/scheduled with the provider and eligible to be called off before it goes out.</summary>
    public static bool IsCancelable(string status) =>
        string.Equals(status, Scheduled, StringComparison.OrdinalIgnoreCase);

    /// <summary>The message did not reach the shopper (a resend is the operator's remedy).</summary>
    public static bool DidNotReach(string status) =>
        string.Equals(status, Undelivered, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, SendFailed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Canceled, StringComparison.OrdinalIgnoreCase);
}
