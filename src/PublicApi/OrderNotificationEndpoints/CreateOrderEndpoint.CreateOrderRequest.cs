using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address; sensible defaults are used when omitted.</summary>
    public AddressDto? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
