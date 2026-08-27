using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Body { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification notification, bool includeBody = false)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Status = notification.Status,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor,
            ContentRedacted = notification.ContentRedacted,
            Body = includeBody ? notification.Body : null
        };
    }
}
