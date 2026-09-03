using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Percentage of calls made in each state.
/// </summary>
public record CallStatePercentage1
{
    /// <summary>
    /// Percentage of completed outbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("completed")]
    public double? Completed { get; init; }

    /// <summary>
    /// Percentage of failed outbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fail")]
    public double? Fail { get; init; }

    /// <summary>
    /// Percentage of busy outbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("busy")]
    public double? Busy { get; init; }

    /// <summary>
    /// Percentage of no-answer outbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("noanswer")]
    public double? Noanswer { get; init; }

    /// <summary>
    /// Percentage of canceled outbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("canceled")]
    public double? Canceled { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
