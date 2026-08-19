using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// The availability of the item.
/// </summary>
public record Availability1
{
    /// <summary>
    /// Whether the item is available.
    /// </summary>
    [JsonPropertyName("inStock")]
    public required bool InStock { get; init; }

    /// <summary>
    /// Human-readable availability text.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
