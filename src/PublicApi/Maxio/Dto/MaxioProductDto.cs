using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Dto;

public class MaxioProductDto
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
    public MaxioProductFamilyDto? ProductFamily { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string ProductPricePointName { get; set; } = string.Empty;

    [JsonPropertyName("product_price_point_id")]
    public int ProductPricePointId { get; set; }
}

public class MaxioProductFamilyDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}

public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProductDto Product { get; set; } = new();
}
