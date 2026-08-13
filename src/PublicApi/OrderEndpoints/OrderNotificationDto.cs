using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// A shopper-facing view of one notification: where it got to. The destination number and the
/// message text are deliberately omitted.
/// </summary>
public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public bool IsScheduled { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        IsScheduled = n.IsScheduled,
        ContentRedacted = n.ContentRedacted,
        CreatedAt = n.CreatedAt
    };
}
