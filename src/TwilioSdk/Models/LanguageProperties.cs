using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

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
