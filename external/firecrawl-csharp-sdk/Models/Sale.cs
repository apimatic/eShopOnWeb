using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Sale/discount information for the variant, present when the variant is discounted.
/// </summary>
public record Sale
{
    /// <summary>
    /// The original (pre-discount) price of the variant.
    /// </summary>
    [JsonPropertyName("originalPrice")]
    public required OriginalPrice OriginalPrice { get; init; }
}
