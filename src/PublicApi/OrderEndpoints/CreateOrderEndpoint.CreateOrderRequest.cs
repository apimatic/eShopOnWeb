using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : CancellableRequest
{
    public List<OrderItemDto> Items { get; set; } = new();

    public AddressDto? ShipToAddress { get; set; }

    /// <summary>Set from the caller's token by the endpoint; never accepted from the request body.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}
