using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ConversationsV2Address
{
    /// <summary>
    /// The channel for Communication.
    /// </summary>
    [JsonPropertyName("channel")]
    public required Channel2 Channel { get; init; }

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
    /// Channel-specific ID for correlating Communications.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channelId")]
    public string? ChannelId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
