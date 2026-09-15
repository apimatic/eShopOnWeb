using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderLineRequest> Items { get; set; } = new();
    public CreateOrderAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = "123 Main St";
    public string City { get; set; } = "Seattle";
    public string State { get; set; } = "WA";
    public string Country { get; set; } = "USA";
    public string ZipCode { get; set; } = "98101";
}
