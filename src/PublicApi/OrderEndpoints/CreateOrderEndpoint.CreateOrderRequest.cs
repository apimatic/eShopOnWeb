using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Places an order from catalog items. Amounts are priced from the catalog in USD.</summary>
public class CreateOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order (at least one).</summary>
    [Required]
    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional ship-to address; a placeholder is used when omitted.</summary>
    public ShipToAddressRequest? ShipToAddress { get; set; }

    /// <summary>Set server-side from the JWT; never bound from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}
