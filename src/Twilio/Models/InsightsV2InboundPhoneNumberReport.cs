using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record InsightsV2InboundPhoneNumberReport
{
    /// <summary>
    /// Inbound phone number handle represented in the report.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    /// <summary>
    /// Total number of calls made with the given handle during the report period.
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
    /// Percentage of calls made in each state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_state_percentage")]
    public CallStatePercentage? CallStatePercentage { get; init; }

    /// <summary>
    /// Percentage of inbound calls with silence tags over total outbound calls. A silent tag is indicative of a connectivity issue or muted audio.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("silent_calls_percentage")]
    public double? SilentCallsPercentage { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
