using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>One requested order line: a catalog item id and how many of it.</summary>
public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }

    public static OrderItemDto From(OrderItem item) => new()
    {
        CatalogItemId = item.ItemOrdered.CatalogItemId,
        ProductName = item.ItemOrdered.ProductName,
        UnitPrice = item.UnitPrice,
        Units = item.Units
    };
}

/// <summary>An order, optionally with the state of its notifications attached.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();

    public static OrderDto From(Order order, IEnumerable<NotificationDto>? notifications = null) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Items = order.OrderItems.Select(OrderItemDto.From).ToList(),
        Notifications = notifications?.ToList() ?? new List<NotificationDto>()
    };
}
