using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Viewport
{
    /// <summary>
    /// The width of the viewport in pixels
    /// </summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>
    /// The height of the viewport in pixels
    /// </summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }
}
