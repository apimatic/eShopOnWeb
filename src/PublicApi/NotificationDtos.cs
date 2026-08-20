using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
}

public class ShopperOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public static class NotificationDtoFactory
{
    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind,
            ProviderStatus = notification.ProviderStatus,
            ProviderMessageSid = string.IsNullOrWhiteSpace(notification.ProviderMessageSid)
                ? null
                : notification.ProviderMessageSid,
            ErrorCode = notification.ProviderErrorCode,
            ErrorMessage = notification.ProviderErrorMessage,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            CreatedAt = notification.CreatedAt,
            ScheduledAt = notification.ScheduledAt
        };
    }

    public static ShopperOrderDto From(Order order, IEnumerable<OrderNotification> notifications)
    {
        return new ShopperOrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Notifications = notifications.Select(From).ToList()
        };
    }

    public static object FromReconciliation(ReconciliationReport report)
    {
        return new
        {
            from = report.From,
            to = report.To,
            fromNumber = report.FromNumber,
            matched = report.Matched,
            providerOnly = report.ProviderOnly,
            applicationOnly = report.ApplicationOnly
        };
    }
}
