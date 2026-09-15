using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderSid { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public int? OriginalNotificationId { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public static class OrderNotificationDtoMapper
{
    public static OrderNotificationDto From(Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotification notification)
        => new()
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ProviderSid = notification.ProviderSid,
            DeliveryStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            CreatedAt = notification.CreatedAt,
            ScheduledSendAt = notification.ScheduledSendAt,
            OriginalNotificationId = notification.OriginalNotificationId
        };
}
