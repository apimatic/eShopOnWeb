using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Answer rate for each STIR/SHAKEN attestation category.
/// </summary>
public record AnswerRate
{
    /// <summary>
    /// Answer rate for Stir Shaken category A.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stsh_a")]
    public double? StshA { get; init; }

    /// <summary>
    /// Answer rate for Stir Shaken category B.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stsh_b")]
    public double? StshB { get; init; }

    /// <summary>
    /// Answer rate for Stir Shaken category C.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stsh_c")]
    public double? StshC { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
