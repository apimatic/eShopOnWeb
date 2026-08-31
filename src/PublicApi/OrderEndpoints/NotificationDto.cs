using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Body { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Type = n.Type.ToString(),
        Status = n.LastKnownStatus,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor,
        ContentRedacted = n.ContentRedacted,
        Body = n.ContentRedacted ? null : n.Body
    };
}
