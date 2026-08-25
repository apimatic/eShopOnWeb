using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineItem>? Items { get; set; }
    public ShippingAddressDto ShippingAddress { get; set; } = new();
}

public class OrderLineItem
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
