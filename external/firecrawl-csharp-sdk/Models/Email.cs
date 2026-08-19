using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Email
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("recipients")]
    [MaxLength(25)]
    public IReadOnlyList<string>? Recipients { get; init; }

    /// <summary>
    /// Include changed page details in email summaries.
    /// </summary>
    [JsonPropertyName("includeDiffs")]
    public bool? IncludeDiffs { get; init; } = false;
}
