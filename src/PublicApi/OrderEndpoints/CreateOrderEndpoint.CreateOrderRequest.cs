using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order.</summary>
    public List<OrderItemRequest> Items { get; set; } = new();

    /// <summary>
    /// The buyer placing the order. This is always set from the authenticated token by the endpoint;
    /// any value sent in the request body is ignored.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? BuyerId { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }

    public int Quantity { get; set; }
}
