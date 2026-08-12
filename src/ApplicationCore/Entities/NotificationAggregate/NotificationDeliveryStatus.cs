using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known delivery-status values. The provider's own status strings
/// (queued, sending, sent, delivered, undelivered, failed, accepted, scheduled, canceled, ...)
/// are stored verbatim so the record reflects what the provider owns. A couple of local-only
/// markers are used for the cases the provider never assigned a status to.
/// </summary>
public static class NotificationDeliveryStatus
{
    // Provider statuses (subset we reason about explicitly).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";

    /// <summary>Local marker: the send could not even be handed to the provider (e.g. network error).</summary>
    public const string SendError = "send_error";

    /// <summary>True when the provider says (or would say) the message never reached the handset.</summary>
    public static bool IsFailure(string? status) =>
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Undelivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, SendError, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Canceled, StringComparison.OrdinalIgnoreCase);
}
