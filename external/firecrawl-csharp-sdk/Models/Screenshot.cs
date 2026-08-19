using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Screenshot
{
    [JsonPropertyName("type")]
    public required Type7 Type { get; init; }

    /// <summary>
    /// Whether to capture a full-page screenshot (ignores viewport.height) or limit to the current viewport.
    /// </summary>
    [JsonPropertyName("fullPage")]
    public bool? FullPage { get; init; } = false;

    /// <summary>
    /// The quality of the screenshot, from 1 to 100. 100 is the highest quality.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quality")]
    public int? Quality { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("viewport")]
    public Viewport? Viewport { get; init; }
}
