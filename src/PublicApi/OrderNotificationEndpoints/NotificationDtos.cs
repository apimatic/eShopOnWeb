using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// The state of a single notification message. Deliberately omits the destination number (PII).
/// Carries the provider's identifier and current delivery outcome so an operator can act on it.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }
    public string? DispatchError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastRefreshedAt { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>Maps domain entities to the API's response DTOs, keeping the destination number out of responses.</summary>
public static class NotificationMapping
{
    public static NotificationDto ToDto(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        IsScheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        DispatchError = n.DispatchError,
        CreatedAt = n.CreatedAt,
        LastRefreshedAt = n.LastRefreshedAt
    };

    public static List<NotificationDto> ToDtos(IEnumerable<OrderNotification> notifications)
        => notifications.Select(ToDto).ToList();

    public static OrderLineDto ToDto(OrderItem item) => new()
    {
        CatalogItemId = item.ItemOrdered.CatalogItemId,
        ProductName = item.ItemOrdered.ProductName,
        UnitPrice = item.UnitPrice,
        Units = item.Units
    };

    public static OrderDto ToDto(OrderWithNotifications orderWithNotifications)
    {
        var order = orderWithNotifications.Order;
        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(ToDto).ToList(),
            Notifications = ToDtos(orderWithNotifications.Notifications)
        };
    }
}
