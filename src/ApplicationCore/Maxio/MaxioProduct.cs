using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscribable plan (Maxio "product") within a product family.
/// </summary>
public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

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
}

public class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}
