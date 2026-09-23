using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Percentage of calls made in each state.
/// </summary>
public record CallStatePercentage
{
    /// <summary>
    /// Percentage of completed inbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("completed")]
    public double? Completed { get; init; }

    /// <summary>
    /// Percentage of failed inbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fail")]
    public double? Fail { get; init; }

    /// <summary>
    /// Percentage of busy inbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("busy")]
    public double? Busy { get; init; }

    /// <summary>
    /// Percentage of no-answer inbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("noanswer")]
    public double? Noanswer { get; init; }

    /// <summary>
    /// Percentage of canceled inbound calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("canceled")]
    public double? Canceled { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
