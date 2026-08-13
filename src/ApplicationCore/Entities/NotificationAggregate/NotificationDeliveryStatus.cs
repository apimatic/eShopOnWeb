using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery-status vocabulary. Most values mirror the provider's own message statuses verbatim
/// (queued, sending, sent, delivered, undelivered, failed, scheduled, canceled, accepted); a
/// couple are local statuses used when we never obtained a provider record at all.
/// </summary>
public static class NotificationDeliveryStatus
{
    /// <summary>The provider accepted the message and confirmed final delivery to the handset.</summary>
    public const string Delivered = "delivered";

    /// <summary>The message was scheduled with the provider for a future time and has not gone out yet.</summary>
    public const string Scheduled = "scheduled";

    /// <summary>A scheduled message was called off before it was sent.</summary>
    public const string Canceled = "canceled";

    /// <summary>
    /// Local status: the provider call to create the message failed outright, so no message id
    /// exists. The underlying order operation still succeeded.
    /// </summary>
    public const string SendFailed = "send_failed";

    /// <summary>
    /// True for a status that represents a settled, non-deliverable outcome the operator may
    /// legitimately want to re-send (anything that is neither delivered nor still in flight).
    /// </summary>
    public static bool CanBeResent(string? status)
    {
        if (string.IsNullOrEmpty(status)) return true;
        return status switch
        {
            Delivered => false,
            "queued" => false,
            "sending" => false,
            "accepted" => false,
            Scheduled => false,
            _ => true
        };
    }

    /// <summary>True once the provider will report no further change for this status.</summary>
    public static bool IsTerminal(string? status)
    {
        return status is Delivered or "undelivered" or "failed" or Canceled or SendFailed;
    }
}
