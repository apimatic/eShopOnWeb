using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record Cube
{
    /// <summary>
    /// Name of the cube, used as a reference in queries
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Human-readable description of what the cube represents
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// List of measures available in the cube, representing quantitative values that can be aggregated
    /// </summary>
    [JsonPropertyName("measures")]
    public required IReadOnlyList<Measure> Measures { get; init; }

    /// <summary>
    /// List of dimensions available in the cube, representing categorical attributes for grouping data
    /// </summary>
    [JsonPropertyName("dimensions")]
    public required IReadOnlyList<Dimension> Dimensions { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
