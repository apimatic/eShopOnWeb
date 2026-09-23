using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

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
