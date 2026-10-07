using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address.</summary>
    public ShippingAddress? ShipToAddress { get; set; }

    /// <summary>Taken from the bearer token, never from the body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>The configured payment currency, for the response.</summary>
    [JsonIgnore]
    public string Currency { get; set; } = string.Empty;
}

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddress
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}
