using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record ExecuteJavaScript
{
    /// <summary>
    /// Execute JavaScript code on the page
    /// </summary>
    [JsonPropertyName("type")]
    public required Type26 Type { get; init; }

    /// <summary>
    /// JavaScript code to execute
    /// </summary>
    [JsonPropertyName("script")]
    public required string Script { get; init; }
}
