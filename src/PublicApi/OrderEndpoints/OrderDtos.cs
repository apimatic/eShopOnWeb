using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body of <c>POST /api/orders</c>: catalog item ids and quantities.</summary>
public class PlaceOrderRequest
{
    public List<OrderLineRequestDto> Items { get; set; } = new();
}

public class OrderLineRequestDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Response of <c>POST /api/orders</c>. Returns the new order's identifier.</summary>
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
}

public class OrderLineViewDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>How a single notification about an order got on — its identifier and provider-owned state.</summary>
public class NotificationView
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string DeliveryState { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public bool IsScheduledFollowUp { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineViewDto> Items { get; set; } = new();
    public List<NotificationView> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationView> Notifications { get; set; } = new();
}
