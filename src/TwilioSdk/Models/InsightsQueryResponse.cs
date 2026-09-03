using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record InsightsQueryResponse
{
    /// <summary>
    /// Indicates the business domain the query was executed against
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    /// <summary>
    /// Array of result objects containing the query results. Each object contains properties matching the requested measures and dimensions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("items")]
    public IReadOnlyList<object>? Items { get; init; }

    /// <summary>
    /// Pagination metadata containing navigation tokens and result information,
    /// this schema should according to convention be added to the response
    /// payload's 'meta' attribute
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meta")]
    public PaginationMeta1? Meta { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
