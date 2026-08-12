using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi.Extensions;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest
{
    /// <summary>The catalog items and quantities to order.</summary>
    public List<OrderLineRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address; a default is used when omitted.</summary>
    public AddressRequest? ShipToAddress { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public class PlaceOrderResponse
{
    /// <summary>Identifier of the created order (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An operator action's result (dispatch / cancel).</summary>
public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();

    /// <summary>Where each of this order's notifications got to.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>Empty request types for endpoints whose input comes only from the token or route.</summary>
public class MyOrdersRequest
{
}

public class OrderIdRequest
{
    public int OrderId { get; set; }

    public OrderIdRequest(int orderId) => OrderId = orderId;
}
