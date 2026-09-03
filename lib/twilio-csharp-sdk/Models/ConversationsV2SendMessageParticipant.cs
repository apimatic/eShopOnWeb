using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

/// <summary>
/// Identifies a participant for an Action. Supports three resolution modes:
/// 1. participantId + channel: Resolves address from participant's registered addresses
/// 2. participantId only: Resolves when participant has exactly one address
/// 3. address + channel: Uses explicit address
/// </summary>
public record ConversationsV2SendMessageParticipant
{
    /// <summary>
    /// Participant ID to resolve address from.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }

    /// <summary>
    /// Explicit address formatted according to channel type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>
    /// Channel type for address resolution.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel")]
    public Channel3? Channel { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
