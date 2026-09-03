using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Number of calls made in each state.
/// </summary>
public record CallState
{
    /// <summary>
    /// Number of completed calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("completed")]
    public int? Completed { get; init; }

    /// <summary>
    /// Number of failed calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fail")]
    public int? Fail { get; init; }

    /// <summary>
    /// Number of busy calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("busy")]
    public int? Busy { get; init; }

    /// <summary>
    /// Number of no-answer calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("noanswer")]
    public int? Noanswer { get; init; }

    /// <summary>
    /// Number of canceled calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("canceled")]
    public int? Canceled { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
