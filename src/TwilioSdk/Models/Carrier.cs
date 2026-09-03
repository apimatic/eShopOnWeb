using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record Carrier
{
    /// <summary>
    /// The name of the carrier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("carrier")]
    public string? CarrierValue { get; init; }

    /// <summary>
    /// Total number of outbound calls for the carrier in the country.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_calls")]
    public int? TotalCalls { get; init; }

    /// <summary>
    /// Total number of blocked outbound calls for the carrier in the country.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blocked_calls")]
    public int? BlockedCalls { get; init; }

    /// <summary>
    /// Percentage of blocked outbound calls for the carrier in the country.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blocked_calls_percentage")]
    public double? BlockedCallsPercentage { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
