using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Extract best-quality video from supported video URLs, e.g. YouTube. Returns a signed GCS URL.
/// </summary>
public record Video
{
    [JsonPropertyName("type")]
    public required Type14 Type { get; init; }
}
