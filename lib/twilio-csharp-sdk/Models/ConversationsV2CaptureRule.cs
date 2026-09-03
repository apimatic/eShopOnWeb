using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Defines a capture rule with from and to addresses. Supports wildcard <c>*</c> for omnidirectional matching.
/// </summary>
public record ConversationsV2CaptureRule
{
    /// <summary>
    /// The from address. Use <c>*</c> for wildcard to match any from address.
    /// </summary>
    [JsonPropertyName("from")]
    public required string From { get; init; }

    /// <summary>
    /// The to address. Use <c>*</c> for wildcard to match any to address.
    /// </summary>
    [JsonPropertyName("to")]
    public required string To { get; init; }

    /// <summary>
    /// Additional matching criteria for the capture rule. For voice calls, can include <c>callType</c> (<c>PSTN</c>, <c>SIP</c>, and similar).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
