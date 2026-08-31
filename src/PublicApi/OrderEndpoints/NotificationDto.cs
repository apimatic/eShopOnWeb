using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>Latest known delivery outcome (queued/scheduled/sent/delivered/undelivered/failed/canceled/SendFailed).</summary>
    public string Status { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public bool ContentRedacted { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static NotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ProviderErrorCode = n.ProviderErrorCode,
        ScheduledFor = n.ScheduledFor,
        CreatedAt = n.CreatedAt,
        LastUpdatedAt = n.LastUpdatedAt,
        ContentRedacted = n.ContentRedacted,
        ResendOfNotificationId = n.ResendOfNotificationId
    };
}
