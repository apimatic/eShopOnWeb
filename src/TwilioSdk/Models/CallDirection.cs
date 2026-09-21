using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Number of calls made in each direction.
/// </summary>
public record CallDirection
{
    /// <summary>
    /// Number of outbound calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound")]
    public int? Outbound { get; init; }

    /// <summary>
    /// Number of inbound calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inbound")]
    public int? Inbound { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
