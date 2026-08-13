using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An order plus where each of its notifications got to.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();

    public static OrderSummaryDto From(OrderWithNotifications source)
    {
        var order = source.Order;
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderLineDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Notifications = source.Notifications.Select(NotificationDto.From).ToList()
        };
    }
}
