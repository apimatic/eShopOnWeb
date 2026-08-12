using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>One of the caller's orders, with its items and where each of its notifications got to.</summary>
public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();

    public static MyOrderDto From(Order order, IEnumerable<NotificationDto> notifications) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Items = order.OrderItems.Select(MyOrderItemDto.From).ToList(),
        Notifications = notifications.ToList()
    };
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }

    public static MyOrderItemDto From(OrderItem i) => new()
    {
        CatalogItemId = i.ItemOrdered.CatalogItemId,
        ProductName = i.ItemOrdered.ProductName,
        UnitPrice = i.UnitPrice,
        Units = i.Units
    };
}
