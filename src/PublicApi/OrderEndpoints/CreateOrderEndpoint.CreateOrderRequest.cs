using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>Taken from the caller's token, never from the body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address.</summary>
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}
