using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio;

public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
    [JsonPropertyName("product_family")]
    public MaxioProductFamilyRef? ProductFamily { get; set; }
    [JsonPropertyName("price_points")]
    public List<MaxioPricePoint>? PricePoints { get; set; }
}

public class MaxioPricePoint
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
    [JsonPropertyName("price_in_cents")]
    public int? PriceInCents { get; set; }
    [JsonPropertyName("interval")]
    public int? Interval { get; set; }
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }
}
