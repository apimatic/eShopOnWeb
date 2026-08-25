using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>Set from the caller's JWT identity - never bound from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = "";

    public List<OrderItemLineRequest> Items { get; set; } = new();
    public ShipToAddressRequest ShipToAddress { get; set; } = new();
}

public class OrderItemLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Country { get; set; } = "";
    public string ZipCode { get; set; } = "";
}
