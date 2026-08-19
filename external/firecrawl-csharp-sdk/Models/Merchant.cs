using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// The merchant the menu belongs to.
/// </summary>
public record Merchant
{
    /// <summary>
    /// The merchant name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The merchant type (e.g. 'restaurant').
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
