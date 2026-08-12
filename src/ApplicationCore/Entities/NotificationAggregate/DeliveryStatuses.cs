using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known delivery status values. The provider-owned values (queued, sent, delivered,
/// undelivered, failed, scheduled, canceled, ...) are stored verbatim as strings; a couple
/// of app-only values cover the cases where the provider never accepted the message.
/// </summary>
public static class DeliveryStatuses
{
    // Provider-owned values (Twilio Message.status)
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Accepted = "accepted";

    // App-only values (the provider never took the message)
    public const string SendFailed = "send_failed";
    public const string Pending = "pending";

    /// <summary>
    /// A status the provider will not move on from, so there is no point re-querying it.
    /// </summary>
    public static bool IsFinal(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return false;
        }

        return status.Equals(Delivered, StringComparison.OrdinalIgnoreCase)
            || status.Equals(Undelivered, StringComparison.OrdinalIgnoreCase)
            || status.Equals(Failed, StringComparison.OrdinalIgnoreCase)
            || status.Equals(Canceled, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The message reached the shopper (or is confirmed en route to a real handset).
    /// </summary>
    public static bool IsDelivered(string? status)
        => !string.IsNullOrEmpty(status) && status.Equals(Delivered, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The message did not reach the shopper — a candidate for an operator resend.
    /// </summary>
    public static bool IsFailure(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return false;
        }

        return status.Equals(Undelivered, StringComparison.OrdinalIgnoreCase)
            || status.Equals(Failed, StringComparison.OrdinalIgnoreCase)
            || status.Equals(SendFailed, StringComparison.OrdinalIgnoreCase);
    }
}
