using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order.</summary>
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address. A placeholder is used when omitted.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }
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
