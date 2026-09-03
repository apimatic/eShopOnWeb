using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record InsightsV2OutboundPhoneNumberReport
{
    /// <summary>
    /// The outbound phone number handle.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    /// <summary>
    /// Total number of outbound calls made with the given handle during the report period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_calls")]
    public int? TotalCalls { get; init; }

    /// <summary>
    /// The call answer score measures customers behavior to the delivered calls.
    /// The score is a value between 0 and 100, where 100 indicates that all calls were successfully answered.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_answer_score")]
    public double? CallAnswerScore { get; init; }

    /// <summary>
    /// Number of calls made with each device type.
    /// <c>voip</c>, <c>mobile</c>, <c>landline</c>, <c>unknown</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("calls_by_device_type")]
    public IReadOnlyDictionary<string, int>? CallsByDeviceType { get; init; }

    /// <summary>
    /// Answer rate for each device type.
    /// <c>voip</c>, <c>mobile</c>, <c>landline</c>, <c>unknown</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answer_rate_device_type")]
    public IReadOnlyDictionary<string, double>? AnswerRateDeviceType { get; init; }

    /// <summary>
    /// Percentage of calls made in each state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_state_percentage")]
    public CallStatePercentage1? CallStatePercentage { get; init; }

    /// <summary>
    /// Percentage of blocked calls by carrier per country.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blocked_calls_by_carrier")]
    public IReadOnlyList<CountyCarrierValue>? BlockedCallsByCarrier { get; init; }

    /// <summary>
    /// Percentage of calls with silence tags over total calls. A silent tag is indicative of a connectivity issue or muted audio.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("silent_calls_percentage")]
    public double? SilentCallsPercentage { get; init; }

    /// <summary>
    /// Percentage of completed outbound calls under 10 seconds (PSTN Short call tags); More than 15% is typically low trust measured.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("short_duration_calls_percentage")]
    public double? ShortDurationCallsPercentage { get; init; }

    /// <summary>
    /// Percentage of long duration calls ( &gt;= 60 seconds)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("long_duration_calls_percentage")]
    public double? LongDurationCallsPercentage { get; init; }

    /// <summary>
    /// Percentage of completed outbound calls to unassigned or unallocated phone numbers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("potential_robocalls_percentage")]
    public double? PotentialRobocallsPercentage { get; init; }

    /// <summary>
    /// Number of calls made in answering machine detection (AMD) enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answering_machine_detection")]
    public AnsweringMachineDetection1? AnsweringMachineDetection { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
