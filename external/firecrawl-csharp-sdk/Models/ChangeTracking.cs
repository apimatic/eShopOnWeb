using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record ChangeTracking
{
    [JsonPropertyName("type")]
    public required Type9 Type { get; init; }

    /// <summary>
    /// The mode to use for change tracking. 'git-diff' provides a detailed diff, and 'json' compares extracted JSON data.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("modes")]
    public IReadOnlyList<Mode>? Modes { get; init; }

    /// <summary>
    /// Schema for JSON extraction when using 'json' mode. Defines the structure of data to extract and compare. Must conform to <see href="https://json-schema.org/">JSON Schema</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("schema")]
    public object? Schema { get; init; }

    /// <summary>
    /// Prompt to use for change tracking when using 'json' mode. If not provided, the default prompt will be used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>
    /// Tag to use for change tracking. Tags can separate change tracking history into separate "branches", where change tracking with a specific tagwill only compare to scrapes made in the same tag. If not provided, the default tag (null) will be used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tag")]
    public string? Tag { get; init; }
}
