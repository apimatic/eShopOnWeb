using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items. The request carries catalog item ids and quantities; the
/// caller's identity is taken from the token, never from the body.
/// </summary>
public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address. When omitted a placeholder is used (invoicing does not use it).</summary>
    public CreateOrderAddress? ShipToAddress { get; set; }

    /// <summary>Set server-side from the authenticated caller; any value sent by the client is ignored.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddress
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
