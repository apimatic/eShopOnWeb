using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What became of a single message. Deliberately does not expose the destination number or the message
/// body: the shopper's number is never surfaced, and disposed-of content is gone.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }

    /// <summary>Which order event this message was for (OrderPlaced, OrderDispatched, DeliveryFollowUp, OrderCancelled, Resend).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's current delivery outcome (e.g. queued, scheduled, sent, delivered, undelivered, failed, canceled).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>True when this message is queued with the provider for future delivery.</summary>
    public bool Scheduled { get; set; }

    public DateTimeOffset? ScheduledSendAt { get; set; }

    /// <summary>The provider's own identifier for the message; null if the provider never accepted it.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>True once the message content has been disposed of on the provider's side.</summary>
    public bool ContentRedacted { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public static NotificationDto From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        Scheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        ProviderMessageSid = n.ProviderMessageSid,
        ContentRedacted = n.ContentRedacted,
        CreatedDate = n.CreatedDate
    };
}
