using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record MessagingV2ChannelsSenderRequestsUpdate
{
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
