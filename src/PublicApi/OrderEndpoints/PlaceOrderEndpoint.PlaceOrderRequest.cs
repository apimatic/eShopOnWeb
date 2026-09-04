using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items. The caller's identity comes from the token.
/// </summary>
public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderLineDto> Items { get; set; } = new();

    public PlaceOrderAddressDto? ShipTo { get; set; }
}

public class PlaceOrderLineDto
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
