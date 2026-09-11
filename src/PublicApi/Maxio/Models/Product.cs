using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

public class Product
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamily? ProductFamily { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }

    [JsonPropertyName("default_product_price_point_id")]
    public int DefaultProductPricePointId { get; set; }
}

public class ProductResponse
{
    [JsonPropertyName("product")]
    public Product Product { get; set; } = null!;
}

public class ProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
