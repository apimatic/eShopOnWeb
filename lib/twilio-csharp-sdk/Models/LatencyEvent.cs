using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record LatencyEvent
{
    /// <summary>
    /// Latency in milliseconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("latency_ms")]
    public int? LatencyMs { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
