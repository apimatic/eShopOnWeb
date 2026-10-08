using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional; the storefront's default demo address is used when omitted.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }

    /// <summary>Set from the access token, never from the body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
