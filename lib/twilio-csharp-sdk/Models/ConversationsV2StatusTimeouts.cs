using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

/// <summary>
/// Timeout settings for channel status transitions.
/// </summary>
public record ConversationsV2StatusTimeouts
{
    /// <summary>
    /// Inactivity timeout in minutes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inactive")]
    [Minimum(1)]
    public int? Inactive { get; init; }

    /// <summary>
    /// Close timeout in minutes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("closed")]
    [Minimum(1)]
    public int? Closed { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
