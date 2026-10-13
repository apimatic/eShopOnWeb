using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>The lines to order: a catalog item id and the quantity of it.</summary>
    public List<OrderItemRequest> Items { get; set; } = new();
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }

    /// <summary>Number of units of the catalog item to order. Must be greater than zero.</summary>
    public int Quantity { get; set; }
}
