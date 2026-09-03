using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record LanguageProperties
{
    /// <summary>
    /// The language key/identifier (typically uppercase)
    /// </summary>
    [JsonPropertyName("key")]
    public required Key Key { get; init; }

    /// <summary>
    /// Human-readable display name for the language
    /// </summary>
    [JsonPropertyName("friendly_name")]
    public required string FriendlyName { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
