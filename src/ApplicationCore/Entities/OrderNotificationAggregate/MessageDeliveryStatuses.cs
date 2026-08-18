using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// The message delivery status values eShop stores. The provider's own values are kept verbatim as
/// lower-case strings; <see cref="SendFailed"/> is the one eShop-local value, used when a message could
/// not be handed to the provider at all.
/// </summary>
public static class MessageDeliveryStatuses
{
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Receiving = "receiving";
    public const string Received = "received";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";

    /// <summary>eShop-local status: the message never reached the provider.</summary>
    public const string SendFailed = "send_failed";

    /// <summary>
    /// A status is terminal when no further provider read can change it, so it need not be refreshed.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or SendFailed => true,
        _ => false
    };
}
