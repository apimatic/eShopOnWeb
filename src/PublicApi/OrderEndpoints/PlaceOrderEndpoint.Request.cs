using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    internal string BuyerId { get; set; } = "";
    public List<OrderItemRequest> Items { get; set; } = new();
    public string ShipToStreet { get; set; } = "";
    public string ShipToCity { get; set; } = "";
    public string ShipToState { get; set; } = "";
    public string ShipToCountry { get; set; } = "";
    public string ShipToZipCode { get; set; } = "";
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
