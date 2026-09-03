using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

/// <summary>
/// Endpoint for a communication participant. Supports three resolution modes:
/// <list type="number">
///   <item><description><b>participantId + channel</b>: Resolves address from participant's registered addresses</description></item>
///   <item><description><b>participantId only</b>: Resolves when participant has exactly one address</description></item>
///   <item><description><b>address + channel</b>: Uses explicit address (for new recipients or cross-channel)</description></item>
/// </list>
/// </summary>
public record ConversationsV2CommunicationEndpoint
{
    /// <summary>
    /// Participant ID to resolve address from. When provided, Conversations looks up
    /// the participant's registered addresses and selects based on channel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }

    /// <summary>
    /// Explicit address formatted according to channel type:
    /// - SMS/VOICE: E.164 phone number (such as "+18005550100")
    /// - WHATSAPP: Phone number with whatsapp prefix (such as "whatsapp:+18005550100")
    /// - RCS: Sender ID or phone number with rcs prefix (such as "rcs:brand_acme_agent" or "rcs:+18005550100")
    /// - CHAT: Customer-defined string identifier
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>
    /// Channel type. Required when participantId has multiple addresses or when using explicit address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel")]
    public Channel4? Channel { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
