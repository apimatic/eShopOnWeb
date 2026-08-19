using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Line height values for different text types.
/// </summary>
public record LineHeights
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("heading")]
    public string? Heading { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("body")]
    public string? Body { get; init; }
}
