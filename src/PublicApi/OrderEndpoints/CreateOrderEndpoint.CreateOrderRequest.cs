using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemDto> Items { get; set; } = new();

    // Optional shipping address; a placeholder is used when omitted.
    public string? ShipToStreet { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToState { get; set; }
    public string? ShipToCountry { get; set; }
    public string? ShipToZipCode { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
