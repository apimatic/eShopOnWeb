using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemLineRequest> Items { get; set; } = new();
}

public class OrderItemLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
