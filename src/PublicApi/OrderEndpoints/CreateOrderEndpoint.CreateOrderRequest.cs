using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [Required]
    public List<OrderLineDto> Items { get; set; } = new();

    public AddressDto? ShipToAddress { get; set; }

    /// <summary>Populated from the JWT; never read from the request body.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}
