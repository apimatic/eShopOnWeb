using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Number of calls made in each answering machine detection.
/// </summary>
public record AnsweringMachineDetection
{
    /// <summary>
    /// Total number of calls with answering machine detection enabled (AMD).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_calls")]
    public int? TotalCalls { get; init; }

    /// <summary>
    /// Percentage of calls marked as answered by human.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answered_by_human_percentage")]
    public double? AnsweredByHumanPercentage { get; init; }

    /// <summary>
    /// Percentage of calls marked as answered by machined related like the following:
    /// <c>machine_start</c>, <c>machine_end_beep</c>, <c>machine_end_silence</c>, <c>machine_end_other</c>, <c>fax</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answered_by_machine_percentage")]
    public double? AnsweredByMachinePercentage { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
