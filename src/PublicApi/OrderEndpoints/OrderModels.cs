using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record PlaceOrderLine(int CatalogItemId, int Quantity);

public record PlaceOrderRequest(List<PlaceOrderLine>? Items);

/// <summary>Response to placing an order. Carries <see cref="OrderId"/> as a top-level field.</summary>
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>The "order placed" notification, when the shopper had a number on file.</summary>
    public NotificationDto? Notification { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }

    public static OrderLineDto From(OrderItem item) => new()
    {
        CatalogItemId = item.ItemOrdered.CatalogItemId,
        ProductName = item.ItemOrdered.ProductName,
        UnitPrice = item.UnitPrice,
        Units = item.Units
    };
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Each notification raised for this order and where it got to.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>Response to an operator dispatch/cancel. Carries <see cref="OrderId"/> and the resulting notifications.</summary>
public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<NotificationDto> Notifications { get; set; } = new();
}
