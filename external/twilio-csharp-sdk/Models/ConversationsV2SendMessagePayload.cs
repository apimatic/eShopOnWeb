using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record ConversationsV2SendMessagePayload
{
    /// <summary>
    /// Identifies a participant for an Action. Supports three resolution modes:
    /// 1. participantId + channel: Resolves address from participant's registered addresses
    /// 2. participantId only: Resolves when participant has exactly one address
    /// 3. address + channel: Uses explicit address
    /// </summary>
    [JsonPropertyName("from")]
    public required ConversationsV2SendMessageParticipant From { get; init; }

    /// <summary>
    /// The recipients of this action.
    /// </summary>
    [JsonPropertyName("to")]
    [MinLength(1)]
    public required IReadOnlyList<ConversationsV2SendMessageParticipant> To { get; init; }

    /// <summary>
    /// Content for a SEND_MESSAGE action.
    /// </summary>
    [JsonPropertyName("content")]
    public required ConversationsV2SendMessageContent Content { get; init; }

    /// <summary>
    /// Channel-specific parameters forwarded as-is to the downstream sending service.
    /// Allows passing backend-specific fields without requiring API changes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channelSettings")]
    public object? ChannelSettings { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
