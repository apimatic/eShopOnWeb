using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body for placing an order: the catalog item ids and quantities to buy.</summary>
public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Set from the authenticated identity; not read from the request body.</summary>
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Response of placing an order — <c>orderId</c> is top-level.</summary>
public class CreateOrderResponse
{
    public int OrderId { get; set; }
}
