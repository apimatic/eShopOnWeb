using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        ProviderSid = notification.ProviderSid,
        ProviderStatus = notification.ProviderStatus,
        Body = notification.ContentRedacted ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted,
        CreatedAt = notification.CreatedAt,
        ScheduledSendAt = notification.ScheduledSendAt,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        ResendOfNotificationId = notification.ResendOfNotificationId
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();

    public static OrderDto From(OrderWithNotifications data)
    {
        var order = data.Order;
        return new OrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Notifications = data.Notifications.Select(NotificationDto.From).ToList()
        };
    }
}
