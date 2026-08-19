using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest
{
    public List<PlaceOrderLine> Items { get; set; } = new();
}

public class PlaceOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderResponse
{
    public PlaceOrderResponse(int orderId)
    {
        OrderId = orderId;
    }

    /// <summary>Identifier of the placed order (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }
}
