using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>Wire model for a Maxio Product (a subscription plan). Mirrors components/schemas/Product.yaml.</summary>
internal sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; set; }

    [JsonPropertyName("default_product_price_point_id")]
    public int? DefaultProductPricePointId { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Nested product family reference. Mirrors components/schemas/Product-Family.yaml (subset).</summary>
internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

/// <summary>Envelope for a single product: <c>{ "product": { ... } }</c>.</summary>
internal sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
