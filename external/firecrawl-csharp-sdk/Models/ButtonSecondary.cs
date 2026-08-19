using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Secondary button styles.
/// </summary>
public record ButtonSecondary
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("background")]
    public string? Background { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("textColor")]
    public string? TextColor { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("borderColor")]
    public string? BorderColor { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("borderRadius")]
    public string? BorderRadius { get; init; }
}
