using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Sorts the provider's own message-status text into the app's three delivery outcomes: done, failed and
/// not-yet. Any value not known to mean done or failed — including an absent/unknown status — is treated as
/// not-yet, never as success.
/// </summary>
public static class NotificationStatusMapper
{
    public static NotificationDeliveryStatus FromProviderStatus(string? providerStatus)
    {
        // Values per Twilio's message-status set (MessageEnumStatus).
        return (providerStatus ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            // done
            "delivered" => NotificationDeliveryStatus.Delivered,
            "sent" => NotificationDeliveryStatus.Sent,
            "read" => NotificationDeliveryStatus.Sent,
            // failed
            "failed" => NotificationDeliveryStatus.Failed,
            "undelivered" => NotificationDeliveryStatus.Failed,
            "canceled" => NotificationDeliveryStatus.Cancelled,
            // not-yet
            "scheduled" => NotificationDeliveryStatus.Scheduled,
            "queued" => NotificationDeliveryStatus.Pending,
            "sending" => NotificationDeliveryStatus.Pending,
            "accepted" => NotificationDeliveryStatus.Pending,
            "receiving" => NotificationDeliveryStatus.Pending,
            "received" => NotificationDeliveryStatus.Pending,
            "partially_delivered" => NotificationDeliveryStatus.Pending,
            _ => NotificationDeliveryStatus.Pending
        };
    }
}
