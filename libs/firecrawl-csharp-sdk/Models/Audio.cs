using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Extract audio (MP3) from supported video URLs, e.g. YouTube. Returns a signed GCS URL.
/// </summary>
public record Audio
{
    [JsonPropertyName("type")]
    public required Type13 Type { get; init; }
}
