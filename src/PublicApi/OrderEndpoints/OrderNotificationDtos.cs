using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Body { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind.ToString(),
        ProviderMessageSid = notification.ProviderMessageSid,
        Status = notification.ProviderStatus,
        ErrorCode = notification.ErrorCode,
        CreatedAt = notification.CreatedAt,
        ScheduledSendAt = notification.ScheduledSendAt,
        ContentRedacted = notification.ContentRedacted,
        Body = notification.ContentRedacted ? null : notification.Body
    };
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
