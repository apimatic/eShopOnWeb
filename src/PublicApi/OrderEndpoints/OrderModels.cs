using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi.Shared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemRequestDto
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

    /// <summary>Maps to the domain address, using placeholders for anything omitted (the order flow is about notifications, not shipping).</summary>
    public Address ToAddress() => new(
        string.IsNullOrWhiteSpace(Street) ? "N/A" : Street,
        string.IsNullOrWhiteSpace(City) ? "N/A" : City,
        State ?? string.Empty,
        string.IsNullOrWhiteSpace(Country) ? "N/A" : Country,
        string.IsNullOrWhiteSpace(ZipCode) ? "00000" : ZipCode);
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequestDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Identifier of the order just placed (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>The notifications this action produced, each with its own notificationId.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId) { }
    public OrderActionResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
