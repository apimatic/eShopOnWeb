using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Network issues of calls for client type. This is indicative of local network issues.
/// </summary>
public record Sdk
{
    /// <summary>
    /// Percentage of ICE connection failure tag that ICE candidates have failed to find compatible connection.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ice_failures_percentage")]
    public double? IceFailuresPercentage { get; init; }

    /// <summary>
    /// Percentage of calls with high latency.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("high_latency_percentage")]
    public double? HighLatencyPercentage { get; init; }

    /// <summary>
    /// Percentage of calls with high packet loss.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("high_packet_loss_percentage")]
    public double? HighPacketLossPercentage { get; init; }

    /// <summary>
    /// Percentage of calls with high jitter.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("high_jitter_percentage")]
    public double? HighJitterPercentage { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
