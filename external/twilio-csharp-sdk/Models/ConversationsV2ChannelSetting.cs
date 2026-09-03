using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Configuration settings for a specific channel type.
/// </summary>
public record ConversationsV2ChannelSetting
{
    /// <summary>
    /// Timeout settings for channel status transitions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusTimeouts")]
    public ConversationsV2StatusTimeouts? StatusTimeouts { get; init; }

    /// <summary>
    /// Array of capture rules with from/to addresses and optional metadata. Use <c>*</c> for wildcard matching in either direction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("captureRules")]
    public IReadOnlyList<ConversationsV2CaptureRule>? CaptureRules { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
