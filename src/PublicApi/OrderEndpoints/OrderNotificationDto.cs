using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public static OrderNotificationDto From(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            ProviderMessageSid = notification.ProviderMessageSid ?? string.Empty,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ContentRedacted = notification.ContentRedacted,
            Body = notification.ContentRedacted ? null : notification.Body,
            CreatedAt = notification.CreatedAt,
            ScheduledSendAt = notification.ScheduledSendAt,
            LastSyncedAt = notification.LastSyncedAt
        };
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListOrderNotificationsResponse()
    {
    }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
