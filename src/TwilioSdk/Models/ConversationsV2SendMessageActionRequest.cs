using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ConversationsV2SendMessageActionRequest
{
    /// <summary>
    /// Action type discriminator. Accepted values: SEND_MESSAGE.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("payload")]
    public required ConversationsV2SendMessagePayload Payload { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
