using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the caller's JWT - ignore any value supplied by the client.</summary>
    public string BuyerId { get; set; } = string.Empty;

    public List<OrderItemRequest> Items { get; set; } = new();

    public string ShipToStreet { get; set; } = string.Empty;
    public string ShipToCity { get; set; } = string.Empty;
    public string ShipToState { get; set; } = string.Empty;
    public string ShipToCountry { get; set; } = string.Empty;
    public string ShipToZipCode { get; set; } = string.Empty;
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
