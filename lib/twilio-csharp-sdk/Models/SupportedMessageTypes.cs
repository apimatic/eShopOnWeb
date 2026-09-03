using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record SupportedMessageTypes
{
    /// <summary>
    /// List of supported message types for opt-out configurations
    /// </summary>
    [JsonPropertyName("message_types")]
    public required IReadOnlyList<MessageTypeConfig> MessageTypes { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
