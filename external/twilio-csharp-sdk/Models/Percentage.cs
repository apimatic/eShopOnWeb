using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Percentage of calls for each STIR/SHAKEN attestation category.
/// </summary>
public record Percentage
{
    /// <summary>
    /// Percentage of calls for Stir Shaken category A.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stsh_a")]
    public double? StshA { get; init; }

    /// <summary>
    /// Percentage of calls for Stir Shaken category B.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stsh_b")]
    public double? StshB { get; init; }

    /// <summary>
    /// Percentage of calls for Stir Shaken category C.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stsh_c")]
    public double? StshC { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
