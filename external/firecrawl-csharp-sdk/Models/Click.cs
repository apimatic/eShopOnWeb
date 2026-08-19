using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Click
{
    /// <summary>
    /// Click on an element
    /// </summary>
    [JsonPropertyName("type")]
    public required Type21 Type { get; init; }

    /// <summary>
    /// Query selector to find the element by
    /// </summary>
    [JsonPropertyName("selector")]
    public required string Selector { get; init; }

    /// <summary>
    /// Clicks all elements matched by the selector, not just the first one. Does not throw an error if no elements match the selector.
    /// </summary>
    [JsonPropertyName("all")]
    public bool? All { get; init; } = false;
}
