using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Total number of calls for each STIR/SHAKEN attestation category.
/// </summary>
public record CallCount
{
    /// <summary>
    /// Total number of calls for Stir Shaken category A.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stsh_a")]
    public int? StshA { get; init; }

    /// <summary>
    /// Total number of calls for Stir Shaken category B.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stsh_b")]
    public int? StshB { get; init; }

    /// <summary>
    /// Total number of calls for Stir Shaken category C.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stsh_c")]
    public int? StshC { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
