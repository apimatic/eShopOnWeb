using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public string Street { get; set; } = default!;
    public string City { get; set; } = default!;
    public string State { get; set; } = default!;
    public string Country { get; set; } = default!;
    public string ZipCode { get; set; } = default!;
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
