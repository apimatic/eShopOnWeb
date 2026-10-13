using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body of POST /api/orders.</summary>
public class CreateOrderRequest
{
    public List<OrderLine> Items { get; set; } = new();

    /// <summary>Resolved from the authenticated caller's token, not from the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class OrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
}
