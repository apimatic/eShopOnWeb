using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Body of POST /api/orders — catalog item ids and quantities. Identity comes from the token.</summary>
public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted (the model requires one).</summary>
    public ShipToAddressDto? ShipToAddress { get; set; }

    internal string BuyerId { get; set; } = string.Empty;
}

/// <summary>Response of POST /api/orders — carries the new order id at the top level.</summary>
public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>Response of POST /api/orders/{id}/dispatch and /cancel.</summary>
public class OrderStateChangeResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>One of the caller's orders, showing where its notifications got to.</summary>
public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();

    public static MyOrderDto FromEntity(Order order, IEnumerable<ApplicationCore.Entities.NotificationAggregate.Notification> notifications) => new()
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
        Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
    };
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}
