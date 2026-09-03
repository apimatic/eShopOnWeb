using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ConversationsV2ParticipantAddress
{
    /// <summary>
    /// The address value formatted according to channel type:
    /// - SMS/VOICE: E.164 phone number (such as "+18005550100")
    /// - WHATSAPP: Phone number with whatsapp prefix (such as "whatsapp:+18005550100")
    /// - RCS: Sender ID or phone number with rcs prefix (such as "rcs:brand_acme_agent" or "rcs:+18005550100")
    /// - CHAT: Customer-defined string identifier
    /// </summary>
    [JsonPropertyName("address")]
    public required string Address { get; init; }

    /// <summary>
    /// Channel type for the Participant address.
    /// </summary>
    [JsonPropertyName("channel")]
    public required Channel11 Channel { get; init; }

    /// <summary>
    /// Participant ID associated with this address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
