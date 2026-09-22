using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Response containing metadata about available cubes, measures, and dimensions for a domain
/// </summary>
public record InsightsMetadataResponse
{
    /// <summary>
    /// The business domain name for which metadata is being provided
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    /// <summary>
    /// List of data cubes available in the domain, each containing measures and dimensions
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cubes")]
    public IReadOnlyList<Cube>? Cubes { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
