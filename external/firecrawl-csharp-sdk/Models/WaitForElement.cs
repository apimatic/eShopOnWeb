using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record WaitForElement
{
    /// <summary>
    /// Wait for a specific element to appear
    /// </summary>
    [JsonPropertyName("type")]
    public required Type19 Type { get; init; }

    /// <summary>
    /// CSS selector to wait for
    /// </summary>
    [JsonPropertyName("selector")]
    public required string Selector { get; init; }
}
