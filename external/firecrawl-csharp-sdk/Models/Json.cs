using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Json
{
    [JsonPropertyName("type")]
    public required Type8 Type { get; init; }

    /// <summary>
    /// The schema to use for the JSON output. Must conform to <see href="https://json-schema.org/">JSON Schema</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("schema")]
    public object? Schema { get; init; }

    /// <summary>
    /// The prompt to use for the JSON output
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }
}
