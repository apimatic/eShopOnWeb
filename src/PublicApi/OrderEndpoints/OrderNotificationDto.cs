using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string ToNumber { get; set; } = string.Empty;

    /// <summary>Message text; null once the content has been disposed of.</summary>
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public string? ErrorDetail { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification entity) => new()
    {
        NotificationId = entity.Id,
        OrderId = entity.OrderId,
        Type = entity.Type.ToString(),
        Status = entity.Status,
        ProviderMessageSid = entity.ProviderMessageSid,
        ToNumber = entity.ToNumber,
        Body = entity.Body,
        ContentRedacted = entity.ContentRedacted,
        CreatedUtc = entity.CreatedUtc,
        ScheduledForUtc = entity.ScheduledForUtc,
        ErrorDetail = entity.ErrorDetail
    };
}
