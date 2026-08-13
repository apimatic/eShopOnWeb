using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest : BaseRequest
{
    /// <summary>Catalog item ids and quantities to order.</summary>
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address; placeholders are used when omitted.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }

    /// <summary>Set from the token by the endpoint; not part of the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Operator request carrying the order to act on.</summary>
public class OrderOperationRequest : BaseRequest
{
    public int OrderId { get; set; }
}

/// <summary>Carries the caller identity for listing the caller's own orders.</summary>
public class MyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Carries the caller identity and the order whose notifications are requested.</summary>
public class OrderNotificationsRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public int OrderId { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    /// <summary>Identifier of the created order (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }
}

public class OrderOperationResponse : BaseResponse
{
    public OrderOperationResponse(Guid correlationId) : base(correlationId) { }
    public OrderOperationResponse() { }

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

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();

    /// <summary>The order's notifications, each showing where it got to.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
