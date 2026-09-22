using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Metrics related to STIR/SHAKEN attestation A, B, and C for the report period.
/// </summary>
public record StirShaken
{
    /// <summary>
    /// Total number of calls for each STIR/SHAKEN attestation category.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_count")]
    public CallCount? CallCount { get; init; }

    /// <summary>
    /// Percentage of calls for each STIR/SHAKEN attestation category.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("percentage")]
    public Percentage? Percentage { get; init; }

    /// <summary>
    /// Answer rate for each STIR/SHAKEN attestation category.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answer_rate")]
    public AnswerRate? AnswerRate { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
