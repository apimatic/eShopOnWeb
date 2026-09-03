using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record AccountReport
{
    /// <summary>
    /// The call deliverability score measures the network effectiveness in delivering calls by scoring calls reach the intended recipient.
    /// The score is a value between 0 and 100, where 100 indicates that all calls were successfully delivered.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_deliverability_score")]
    public double? CallDeliverabilityScore { get; init; }

    /// <summary>
    /// The call answer score measures customers behavior to the delivered calls.
    /// The score is a value between 0 and 100, where 100 indicates that all calls were successfully answered.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_answer_score")]
    public double? CallAnswerScore { get; init; }

    /// <summary>
    /// Total number of calls made during the report period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_calls")]
    public int? TotalCalls { get; init; }

    /// <summary>
    /// Number of calls made in each direction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_direction")]
    public CallDirection? CallDirection { get; init; }

    /// <summary>
    /// Number of calls made in each state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_state")]
    public CallState? CallState { get; init; }

    /// <summary>
    /// Number of calls made in each type.
    /// <c>carrier</c>, <c>sip</c>, <c>trunking</c>, <c>client</c>, <c>whatsapp</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_type")]
    public CallType? CallType { get; init; }

    /// <summary>
    /// Average length of call in seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("aloc")]
    public double? Aloc { get; init; }

    /// <summary>
    /// Number of calls made in each Twilio Edge location. Refer to <see href="https://www.twilio.com/docs/global-infrastructure/edge-locations#public-edge-locations">Public Edge Locations</see> for more detail.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("twilio_edge_location")]
    public IReadOnlyDictionary<string, int>? TwilioEdgeLocation { get; init; }

    /// <summary>
    /// Number of calls originating from each country (ISO alpha-2).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("caller_country_code")]
    public IReadOnlyDictionary<string, int>? CallerCountryCode { get; init; }

    /// <summary>
    /// Number of calls terminating in each country (ISO alpha-2).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("callee_country_code")]
    public IReadOnlyDictionary<string, int>? CalleeCountryCode { get; init; }

    /// <summary>
    /// Average queue time in milliseconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("average_queue_time_ms")]
    public double? AverageQueueTimeMs { get; init; }

    /// <summary>
    /// Percentage of silent calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("silent_calls_percentage")]
    public double? SilentCallsPercentage { get; init; }

    /// <summary>
    /// Network-quality indicators for SDK and Twilio Gateway traffic during the report period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("network_issues")]
    public NetworkIssues? NetworkIssues { get; init; }

    /// <summary>
    /// Know Your Traffic (KYT) metrics focused on outbound carrier performance and trust signals for the report period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("KYT")]
    public Kyt? Kyt { get; init; }

    /// <summary>
    /// Number of calls made in each answering machine detection.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answering_machine_detection")]
    public AnsweringMachineDetection? AnsweringMachineDetection { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
