using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What was sent for an order and what became of it. Deliberately never exposes the destination number.
/// The message text is included only in the owner's own view and disappears once content is disposed of.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }

    /// <summary>What the message was about (OrderPlaced, OrderDispatched, DeliveryFeedback, OrderCancelled).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>eShop's own view of where the message got to (Sent, Scheduled, Failed, Cancelled, Pending).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the message, which the operator endpoints act on.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string? ProviderStatus { get; set; }

    public int? ProviderErrorCode { get; set; }

    public bool ContentRedacted { get; set; }

    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The message text. Present only in the owner's own view, and null once the content is disposed of.</summary>
    public string? Message { get; set; }

    public static NotificationDto From(Notification n, bool includeMessage)
        => new()
        {
            NotificationId = n.Id,
            OrderId = n.OrderId,
            Kind = n.Kind.ToString(),
            State = n.State.ToString(),
            ProviderMessageSid = n.ProviderMessageSid,
            ProviderStatus = n.ProviderStatus,
            ProviderErrorCode = n.ProviderErrorCode,
            ContentRedacted = n.ContentRedacted,
            ScheduledSendAt = n.ScheduledSendAt,
            SentAt = n.SentAt,
            CreatedAt = n.CreatedAt,
            Message = includeMessage ? n.Body : null
        };
}
