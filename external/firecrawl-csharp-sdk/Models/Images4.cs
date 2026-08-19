using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Images4
{
    /// <summary>
    /// Image URL.
    /// </summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>
    /// Alternative text for the image.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("alt")]
    public string? Alt { get; init; }
}
