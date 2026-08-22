using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ShopOrderEndpoints;

public class CreateShopOrderRequest : BaseRequest
{
    public List<CreateShopOrderItemRequest> Items { get; set; } = new();
}

public class CreateShopOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
