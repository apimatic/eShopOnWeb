using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi;

internal static class NotificationDtoMapper
{
    public static string OrderStatusName(OrderStatus status) => status switch
    {
        OrderStatus.Placed => "placed",
        OrderStatus.Dispatched => "dispatched",
        OrderStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant()
    };

    public static string KindName(OrderNotificationKind kind) => kind switch
    {
        OrderNotificationKind.OrderPlaced => "orderPlaced",
        OrderNotificationKind.OrderDispatched => "orderDispatched",
        OrderNotificationKind.DeliveryFollowUp => "deliveryFollowUp",
        OrderNotificationKind.OrderCancelled => "orderCancelled",
        _ => kind.ToString()
    };

    public static NotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = KindName(notification.Kind),
        ProviderSid = notification.ProviderSid,
        Status = notification.Status,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        Body = notification.BodyRedacted ? null : notification.Body,
        BodyRedacted = notification.BodyRedacted,
        CreatedAt = notification.CreatedAt,
        ScheduledFor = notification.ScheduledFor
    };
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool BodyRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
}
