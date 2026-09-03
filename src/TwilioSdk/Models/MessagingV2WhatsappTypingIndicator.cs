using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// <list type="bullet">
///   <item><description>Payload for typing indicator request, Typing indicator request for WhatsApp channel. Requires a messageId from a recent inbound message.</description></item>
/// </list>
/// </summary>
public record MessagingV2WhatsappTypingIndicator
{
    /// <summary>
    /// Shared channel identifier
    /// </summary>
    [JsonPropertyName("channel")]
    public string Channel { get; } = "whatsapp";

    /// <summary>
    /// Message SID that identifies the conversation thread for the typing indicator. Must be a valid Twilio Message SID (SM*) or Media SID (MM*) from an existing WhatsApp conversation.
    /// </summary>
    [JsonPropertyName("messageId")]
    [RegularExpression("^(SM|MM)[0-9a-fA-F]{32}$")]
    public required string MessageId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
