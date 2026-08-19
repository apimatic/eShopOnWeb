using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Scroll
{
    /// <summary>
    /// Scroll the page or a specific element
    /// </summary>
    [JsonPropertyName("type")]
    public required Type24 Type { get; init; }

    /// <summary>
    /// Direction to scroll
    /// </summary>
    [JsonPropertyName("direction")]
    public Direction? Direction { get; init; } = Direction.Down;

    /// <summary>
    /// Query selector for the element to scroll
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("selector")]
    public string? Selector { get; init; }
}
