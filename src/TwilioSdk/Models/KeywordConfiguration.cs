using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record KeywordConfiguration
{
    /// <summary>
    /// The keyword type in format KeywordType.Locale (e.g., STOP.ENGLISH, HELP.FRENCH)
    /// </summary>
    [JsonPropertyName("keyword_type")]
    public required string KeywordType { get; init; }

    /// <summary>
    /// The message type identifier (typically country codes or special identifiers)
    /// </summary>
    [JsonPropertyName("message_type")]
    public required string MessageType { get; init; }

    /// <summary>
    /// Array of keyword strings for this configuration
    /// </summary>
    [JsonPropertyName("keywords")]
    public required IReadOnlyList<string> Keywords { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
