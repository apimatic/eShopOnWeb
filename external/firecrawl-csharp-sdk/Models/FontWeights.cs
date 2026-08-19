using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Font weight definitions.
/// </summary>
public record FontWeights
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("light")]
    public int? Light { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("regular")]
    public int? Regular { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("medium")]
    public int? Medium { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bold")]
    public int? Bold { get; init; }
}
