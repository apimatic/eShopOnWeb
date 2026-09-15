using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }

    /// <summary>Set from the caller's token; never bound from the request body.</summary>
    public string? BuyerId { get; set; }
}
