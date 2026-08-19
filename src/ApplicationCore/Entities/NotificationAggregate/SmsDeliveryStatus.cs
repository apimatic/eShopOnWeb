using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery outcome of a message, mirroring the states the messaging provider owns
/// plus a couple of local-only states for messages that never left this application.
/// </summary>
public enum SmsDeliveryStatus
{
    /// <summary>Created locally but not yet handed to the provider.</summary>
    Pending = 0,
    /// <summary>The provider accepted the request but has not yet selected a sender.</summary>
    Accepted = 1,
    /// <summary>Queued with the provider for a future send time (a follow-up).</summary>
    Scheduled = 2,
    Queued = 3,
    Sending = 4,
    /// <summary>The nearest upstream carrier accepted the message.</summary>
    Sent = 5,
    Delivered = 6,
    Undelivered = 7,
    Failed = 8,
    Canceled = 9,
    Read = 10,
    PartiallyDelivered = 11,
    Receiving = 12,
    Received = 13
}

public static class SmsDeliveryStatusExtensions
{
    /// <summary>
    /// Maps a raw provider status string (e.g. "delivered") to <see cref="SmsDeliveryStatus"/>.
    /// Unknown values fall back to <see cref="SmsDeliveryStatus.Pending"/>.
    /// </summary>
    public static SmsDeliveryStatus FromProviderStatus(string? providerStatus)
    {
        if (string.IsNullOrWhiteSpace(providerStatus))
            return SmsDeliveryStatus.Pending;

        return providerStatus.Trim().ToLowerInvariant() switch
        {
            "accepted" => SmsDeliveryStatus.Accepted,
            "scheduled" => SmsDeliveryStatus.Scheduled,
            "queued" => SmsDeliveryStatus.Queued,
            "sending" => SmsDeliveryStatus.Sending,
            "sent" => SmsDeliveryStatus.Sent,
            "delivered" => SmsDeliveryStatus.Delivered,
            "undelivered" => SmsDeliveryStatus.Undelivered,
            "failed" => SmsDeliveryStatus.Failed,
            "canceled" or "cancelled" => SmsDeliveryStatus.Canceled,
            "read" => SmsDeliveryStatus.Read,
            "partially_delivered" => SmsDeliveryStatus.PartiallyDelivered,
            "receiving" => SmsDeliveryStatus.Receiving,
            "received" => SmsDeliveryStatus.Received,
            _ => SmsDeliveryStatus.Pending
        };
    }

    /// <summary>
    /// True when the status is a final delivery outcome that should never be overwritten
    /// by a later, stale status update.
    /// </summary>
    public static bool IsTerminal(this SmsDeliveryStatus status) => status switch
    {
        SmsDeliveryStatus.Delivered => true,
        SmsDeliveryStatus.Undelivered => true,
        SmsDeliveryStatus.Failed => true,
        SmsDeliveryStatus.Canceled => true,
        SmsDeliveryStatus.Read => true,
        _ => false
    };

    /// <summary>
    /// True when the shopper was not reached and an operator may legitimately re-send.
    /// </summary>
    public static bool IsUndelivered(this SmsDeliveryStatus status) => status switch
    {
        SmsDeliveryStatus.Undelivered => true,
        SmsDeliveryStatus.Failed => true,
        SmsDeliveryStatus.Canceled => true,
        _ => false
    };
}
