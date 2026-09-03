using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Metrics related to Voice Integrity enabled calls for the report period.
/// </summary>
public record VoiceIntegrity
{
    /// <summary>
    /// Total number of calls with Voice Integrity enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("enabled_calls")]
    public int? EnabledCalls { get; init; }

    /// <summary>
    /// Percentage of calls with Voice Integrity enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("enabled_percentage")]
    public double? EnabledPercentage { get; init; }

    /// <summary>
    /// Number of calls per Voice Integrity enabled Bundle Sid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("calls_per_bundle")]
    public IReadOnlyList<VoiceIntegrityCallsPerBundle>? CallsPerBundle { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
