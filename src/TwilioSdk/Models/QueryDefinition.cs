using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Structured query definition that specifies what data to retrieve and how to filter, group, and order it
/// </summary>
public record QueryDefinition
{
    /// <summary>
    /// Array of measures to retrieve, representing quantitative values or metrics to be calculated
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("measures")]
    public IReadOnlyList<string>? Measures { get; init; }

    /// <summary>
    /// Array of dimensions to retrieve, representing categorical attributes for grouping and organizing data
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dimensions")]
    public IReadOnlyList<string>? Dimensions { get; init; }

    /// <summary>
    /// Nested filter conditions. Always use <c>op</c> and <c>expressions</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("filters")]
    public IReadOnlyList<Filter>? Filters { get; init; }

    /// <summary>
    /// Specifications for sorting the query results by specific fields in ascending or descending order
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("orderBy")]
    public IReadOnlyList<OrderBy>? OrderBy { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
