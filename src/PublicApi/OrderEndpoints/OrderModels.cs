using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>One line of an order: which catalog item and how many.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Optional shipping address for an order placed through the API.</summary>
public class OrderAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Body for placing an order. The caller's identity comes from the token.</summary>
public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public OrderAddressDto? ShippingAddress { get; set; }
}

/// <summary>Response for a placed order; carries the new order id as a top-level field.</summary>
public class CreateOrderResponse
{
    public int OrderId { get; set; }
}
