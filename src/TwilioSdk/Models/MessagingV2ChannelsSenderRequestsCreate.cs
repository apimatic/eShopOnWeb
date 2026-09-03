using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record MessagingV2ChannelsSenderRequestsCreate
{
    /// <summary>
    /// The ID of the sender in <c>whatsapp:&lt;E.164_PHONE_NUMBER&gt;</c> format.
    /// </summary>
    [JsonPropertyName("sender_id")]
    public required string? SenderId { get; init; }

    /// <summary>
    /// The configuration settings for creating a sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("configuration")]
    public MessagingV2ChannelsSenderConfiguration? Configuration { get; init; }

    /// <summary>
    /// The configuration settings for webhooks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhook")]
    public MessagingV2ChannelsSenderWebhook? Webhook { get; init; }

    /// <summary>
    /// The profile information for the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("profile")]
    public MessagingV2ChannelsSenderProfile? Profile { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
