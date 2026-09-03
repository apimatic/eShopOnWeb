using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record VoiceIntegrityCallsPerBundle
{
    /// <summary>
    /// Voice Integrity Approved Profile Sid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bundle_sid")]
    public string? BundleSid { get; init; }

    /// <summary>
    /// The number of Voice Integrity enabled and registered phone numbers per Bundle Sid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("enabled_phonenumbers")]
    public int? EnabledPhonenumbers { get; init; }

    /// <summary>
    /// The number of outbound calls on Voice Integrity enabled and registered number per Bundle Sid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_calls")]
    public int? TotalCalls { get; init; }

    /// <summary>
    /// Answer rate for calls on Voice Integrity enabled and registered number per Bundle Sid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answer_rate")]
    public double? AnswerRate { get; init; }

    /// <summary>
    /// Rate for calls on Voice Integrity enabled and registered number per Bundle Sid that were answered by Human per each use case for Branded bundled calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("human_answer_rate")]
    public double? HumanAnswerRate { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
