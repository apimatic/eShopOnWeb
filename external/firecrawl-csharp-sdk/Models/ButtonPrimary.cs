using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Primary button styles.
/// </summary>
public record ButtonPrimary
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("background")]
    public string? Background { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("textColor")]
    public string? TextColor { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("borderRadius")]
    public string? BorderRadius { get; init; }
}
