using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record NumbersV1CreateEmbeddedSessionResponse
{
    /// <summary>
    /// Registration identifier (BU-prefixed).
    /// </summary>
    [JsonPropertyName("id")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public required string Id { get; init; }

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
