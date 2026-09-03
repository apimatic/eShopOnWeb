using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

/// <summary>
/// Default webhook configuration for Conversation-level events under this Configuration.
/// </summary>
public record ConversationsV2StatusCallbackConfig
{
    /// <summary>
    /// Destination URL for webhooks.
    /// </summary>
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public required string Url { get; init; }

    /// <summary>
    /// HTTP method used to invoke the webhook URL.
    /// </summary>
    [JsonPropertyName("method")]
    public AmdStatusCallbackMethod? Method { get; init; } = AmdStatusCallbackMethod.Post;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
