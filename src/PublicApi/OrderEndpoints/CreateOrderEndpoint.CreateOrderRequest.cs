using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    public List<OrderLineRequest> Items { get; set; } = new List<OrderLineRequest>();

    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
