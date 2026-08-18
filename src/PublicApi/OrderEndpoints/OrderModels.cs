using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A requested order line.</summary>
public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Optional shipping address for the order.</summary>
public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Places an order from catalog items. The caller's identity comes from the token.</summary>
public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

/// <summary>Response to placing an order. The created identifier is a top-level field.</summary>
public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
}

/// <summary>Response to an operator dispatch/cancel action.</summary>
public class OrderTransitionResponse : BaseResponse
{
    public OrderTransitionResponse(Guid correlationId) : base(correlationId) { }
    public OrderTransitionResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>The notifications raised for one order.</summary>
public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

/// <summary>One of the caller's orders with a summary of its notifications.</summary>
public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}
