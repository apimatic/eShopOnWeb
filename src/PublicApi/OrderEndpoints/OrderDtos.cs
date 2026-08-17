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
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
}

public class DispatchOrderRequest : BaseRequest
{
    public DispatchOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; set; }
}

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; set; }
}

public class GetOrderNotificationsRequest : BaseRequest
{
    public GetOrderNotificationsRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; set; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}
