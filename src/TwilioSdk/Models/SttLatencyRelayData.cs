using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record SttLatencyRelayData
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

    [JsonPropertyName("stt_latency")]
    public required LatencyEvent SttLatency { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
