using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// The operator/shopper view of a single notification, including where it got to. The destination
/// number is masked; the message body is never returned. <see cref="NotificationId"/> is what the
/// operator endpoints act on.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>Normalized outcome: cancelled / send_failed / scheduled / the provider's delivery status / unknown.</summary>
    public string DeliveryStatus { get; set; } = string.Empty;

    /// <summary>The provider's own last-known status wire value, when there is one.</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>The provider's identifier for the message — the join key used in reconciliation.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>True while this is a future-dated message the provider still holds.</summary>
    public bool Scheduled { get; set; }

    public DateTimeOffset? ScheduledSendAt { get; set; }

    /// <summary>True once the message could not reach the shopper (undeliverable, or never handed to the provider).</summary>
    public bool DidNotReachRecipient { get; set; }

    /// <summary>True once the message content has been disposed of at the shopper's request.</summary>
    public bool ContentDisposed { get; set; }

    /// <summary>Masked destination (last four digits only) — the number itself is never returned in full.</summary>
    public string To { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = NotificationPresentation.DeliveryStatus(n),
        ProviderStatus = n.ProviderStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        Scheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        DidNotReachRecipient = n.DidNotReachRecipient,
        ContentDisposed = n.ContentDisposed,
        To = NotificationPresentation.Mask(n.ToNumber),
        CreatedAt = n.CreatedAt
    };
}
