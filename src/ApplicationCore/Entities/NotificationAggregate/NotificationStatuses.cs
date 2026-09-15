using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery-status values a notification can hold. These mirror the messaging provider's
/// own status vocabulary (Twilio) so the stored value is the state the provider owns, with the
/// single local addition of <see cref="Pending"/> for the instant before a message is handed over.
/// </summary>
public static class NotificationStatuses
{
    public const string Pending = "pending";
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";

    /// <summary>
    /// A status from which the outcome can no longer change, so there is no point re-querying
    /// the provider for it.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled => true,
        _ => false
    };
}
