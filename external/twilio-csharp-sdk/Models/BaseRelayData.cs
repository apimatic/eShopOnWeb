using System.Text.Json.Serialization;

namespace Twilio.Models;

public record BaseRelayData
{
    /// <summary>
    /// Session id of the conversation relay.
    /// </summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    /// <summary>
    /// Sequence number of the event.
    /// </summary>
    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }
}
