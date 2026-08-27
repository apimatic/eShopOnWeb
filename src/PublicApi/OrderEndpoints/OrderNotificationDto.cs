using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Type = notification.Type.ToString(),
        Status = notification.Status,
        Body = notification.Body,
        ContentDisposed = notification.ContentDisposed,
        ScheduledFor = notification.ScheduledFor,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        ResendOfNotificationId = notification.ResendOfNotificationId,
        CreatedAt = notification.CreatedAt,
        LastUpdatedAt = notification.LastUpdatedAt
    };
}
