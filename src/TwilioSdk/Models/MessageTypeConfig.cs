using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record MessageTypeConfig
{
    /// <summary>
    /// The message type key/identifier (typically country codes or special identifiers)
    /// </summary>
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    /// <summary>
    /// Human-readable display name for the message type
    /// </summary>
    [JsonPropertyName("friendly_name")]
    public required string FriendlyName { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
