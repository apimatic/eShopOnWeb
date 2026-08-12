using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderAddress
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Place an order from catalog items. The caller's identity comes from the token.</summary>
public class PlaceOrderRequest
{
    public List<PlaceOrderLine> Items { get; set; } = new();

    /// <summary>Optional shipping address; a default is used when omitted.</summary>
    public PlaceOrderAddress? ShippingAddress { get; set; }
}

/// <summary>The placed order; <c>orderId</c> is the top-level identifier.</summary>
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class MyOrdersResponse
{
    public IReadOnlyList<OrderView> Orders { get; set; } = new List<OrderView>();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public IReadOnlyList<NotificationView> Notifications { get; set; } = new List<NotificationView>();
}
