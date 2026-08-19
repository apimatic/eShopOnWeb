using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Source
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("source")]
    public string? SourceValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("indexed")]
    public bool? Indexed { get; init; }
}
