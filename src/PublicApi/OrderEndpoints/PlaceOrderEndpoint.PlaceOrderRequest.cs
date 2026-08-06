using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Request to place an order from catalog items.</summary>
public class PlaceOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order.</summary>
    public List<PlaceOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }

    /// <summary>
    /// The owning shopper, set server-side from the bearer token. Has a private setter so it is never
    /// bound from the request body.
    /// </summary>
    public string? BuyerId { get; private set; }

    public void SetBuyer(string buyerId) => BuyerId = buyerId;
}

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
