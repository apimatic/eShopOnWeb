using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Place an order directly from catalog items. The buyer is the token's caller.</summary>
public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted.</summary>
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class OrderLineRequest
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

/// <summary>Response to placing an order: carries the new order id at the top level.</summary>
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>Response to an operator dispatch/cancel action.</summary>
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

/// <summary>A shopper's order together with where each of its notifications got to.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
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

/// <summary>Carrier for the {orderId} route parameter on operator/shopper order sub-routes.</summary>
public class OrderIdRequest
{
    public int OrderId { get; init; }

    public OrderIdRequest(int orderId) => OrderId = orderId;
}
