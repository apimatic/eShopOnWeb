using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body of POST /api/orders. The caller's identity comes from the token, not the body.</summary>
public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address; defaults are used when omitted (this surface is about notifications).</summary>
    public ShippingAddressDto? ShipTo { get; set; }

    [JsonIgnore]
    public string? CallerId { get; set; }
}

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

/// <summary>Response of POST /api/orders. Carries the new id as a top-level field.</summary>
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
}

/// <summary>Response of GET /api/my-orders.</summary>
public class MyOrdersResponse
{
    public List<OrderSummaryView> Orders { get; set; } = new();
}

/// <summary>Response of GET /api/orders/{orderId}/notifications. Each entry carries its own notificationId.</summary>
public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationView> Notifications { get; set; } = new();
}

/// <summary>Simple acknowledgement for the operator dispatch/cancel actions.</summary>
public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
