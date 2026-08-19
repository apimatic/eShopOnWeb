using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Brand images.
/// </summary>
public record Images2
{
    /// <summary>
    /// Logo image URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("logo")]
    public string? Logo { get; init; }

    /// <summary>
    /// Favicon URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("favicon")]
    public string? Favicon { get; init; }

    /// <summary>
    /// Open Graph image URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ogImage")]
    public string? OgImage { get; init; }
}
