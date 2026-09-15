using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemDto> Items { get; set; } = new();

    /// <summary>Optional shipping address; a sample address is used when omitted.</summary>
    public PlaceOrderAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "Placed";
}

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public DispatchOrderRequest(int orderId) => OrderId = orderId;
}

public class DispatchOrderResponse : BaseResponse
{
    public DispatchOrderResponse(Guid correlationId) : base(correlationId) { }
    public DispatchOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = "Dispatched";
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public CancelOrderRequest(int orderId) => OrderId = orderId;
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = "Cancelled";
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }

    /// <summary>Where each of this order's notifications got to.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class GetOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }
    public GetOrderNotificationsRequest(int orderId) => OrderId = orderId;
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
