using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public CreateOrderAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = "123 Main St.";
    public string City { get; set; } = "Kent";
    public string State { get; set; } = "OH";
    public string Country { get; set; } = "United States";
    public string ZipCode { get; set; } = "44240";
}
