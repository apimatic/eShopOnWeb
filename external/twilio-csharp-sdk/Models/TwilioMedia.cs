using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Twilio.Models;

/// <summary>
/// twilio/media is used to send file attachments, or to send long text via MMS in the US and Canada. As such, the twilio/media type must contain at least ONE of text or media content.
/// </summary>
public record TwilioMedia
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("media")]
    public required IReadOnlyList<string> Media { get; init; }
}
