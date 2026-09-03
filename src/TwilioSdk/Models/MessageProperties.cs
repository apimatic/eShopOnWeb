using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record MessageProperties
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
    /// The actual opt-out message text to be sent
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
