using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Request to place an order from catalog items. The caller's identity comes from the token.</summary>
public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted (SMS notifications are the focus here).</summary>
    public AddressDto? ShipToAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

/// <summary>Response to placing an order. Returns the new order's identifier as a top-level field.</summary>
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>Response to an order lifecycle change (dispatch / cancel).</summary>
public class OrderStatusResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>An order in the caller's order list, with where each of its notifications got to.</summary>
public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>The notifications sent for one order, and what became of each.</summary>
public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<NotificationDto> Notifications { get; set; } = new();
}
