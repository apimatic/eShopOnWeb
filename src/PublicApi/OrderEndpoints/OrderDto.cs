using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();

    public static OrderDto FromEntity(Order order, IEnumerable<OrderNotificationDto>? notifications = null) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Items = order.OrderItems.Select(OrderItemDto.FromEntity).ToList(),
        Notifications = notifications?.ToList() ?? new List<OrderNotificationDto>()
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }

    public static OrderItemDto FromEntity(OrderItem entity) => new()
    {
        CatalogItemId = entity.ItemOrdered.CatalogItemId,
        ProductName = entity.ItemOrdered.ProductName,
        UnitPrice = entity.UnitPrice,
        Units = entity.Units
    };
}
