using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// The availability of the variant. Always present on a variant.
/// </summary>
public record Availability
{
    /// <summary>
    /// Whether the variant is in stock.
    /// </summary>
    [JsonPropertyName("inStock")]
    public required bool InStock { get; init; }

    /// <summary>
    /// Human-readable availability text (e.g. 'In Stock').
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
