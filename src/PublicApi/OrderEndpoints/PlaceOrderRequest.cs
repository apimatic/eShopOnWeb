using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderAddressRequest
{
    public string Street { get; set; } = "123 Demo Street";
    public string City { get; set; } = "Seattle";
    public string State { get; set; } = "WA";
    public string Country { get; set; } = "USA";
    public string ZipCode { get; set; } = "98101";
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public PlaceOrderAddressRequest ShipTo { get; set; } = new();
}
