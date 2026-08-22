using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }

    public static OrderNotificationDto From(NotificationResult result)
    {
        return new OrderNotificationDto
        {
            NotificationId = result.NotificationId,
            OrderId = result.OrderId,
            Kind = result.Kind,
            Body = result.Body,
            ContentRedacted = result.ContentRedacted,
            ProviderMessageSid = result.ProviderMessageSid,
            ProviderStatus = result.ProviderStatus,
            ProviderErrorCode = result.ProviderErrorCode,
            ScheduledSendAt = result.ScheduledSendAt,
            CreatedAt = result.CreatedAt,
            ProviderDateSent = result.ProviderDateSent
        };
    }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class ShopperOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();

    public static ShopperOrderDto From(ShopperOrderResult result)
    {
        return new ShopperOrderDto
        {
            OrderId = result.OrderId,
            Status = result.Status,
            OrderDate = result.OrderDate,
            Total = result.Total,
            Items = result.Items.Select(i => new OrderItemDto
            {
                CatalogItemId = i.CatalogItemId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Notifications = result.Notifications.Select(OrderNotificationDto.From).ToList()
        };
    }
}
