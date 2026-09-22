using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record InsightsQueryRequest
{
    /// <summary>
    /// The business domain to execute the query against
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    /// <summary>
    /// Structured query definition that specifies what data to retrieve and how to filter, group, and order it
    /// </summary>
    [JsonPropertyName("query")]
    public required QueryDefinition Query { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
