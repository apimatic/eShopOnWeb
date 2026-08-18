using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// How a single notification is presented over the API. Carries the operator-actionable identifier
/// (<see cref="NotificationId"/>) and the state the provider owns (its message SID and current delivery
/// outcome). The destination number is deliberately not surfaced.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, failed, undelivered, scheduled, canceled, ...).</summary>
    public string? DeliveryStatus { get; set; }

    /// <summary>The provider's message identifier.</summary>
    public string? ProviderMessageSid { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentDisposed { get; set; }

    /// <summary>The message text; null once the content has been disposed of.</summary>
    public string? MessageBody { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = n.DeliveryStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentDisposed = n.ContentDisposed,
        MessageBody = n.Body,
        CreatedAt = n.CreatedAt
    };
}
