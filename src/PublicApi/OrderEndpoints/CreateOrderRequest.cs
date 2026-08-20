using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public CreateOrderAddressRequest? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = "Not specified";
    public string City { get; set; } = "N/A";
    public string State { get; set; } = "N/A";
    public string Country { get; set; } = "N/A";
    public string ZipCode { get; set; } = "00000";
}
