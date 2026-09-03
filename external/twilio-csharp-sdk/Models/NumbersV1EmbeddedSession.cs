using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record NumbersV1EmbeddedSession
{
    /// <summary>
    /// Session ID for the compliance embeddable.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    /// <summary>
    /// Ephemeral session token for the compliance embeddable.
    /// </summary>
    [JsonPropertyName("sessionToken")]
    public required string SessionToken { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
