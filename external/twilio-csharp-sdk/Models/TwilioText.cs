using System.Text.Json.Serialization;

namespace Twilio.Models;

/// <summary>
/// Type containing only plain text-based content
/// </summary>
public record TwilioText
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }
}
