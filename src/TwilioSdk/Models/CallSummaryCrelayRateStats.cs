using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record CallSummaryCrelayRateStats
{
    [JsonPropertyName("min")]
    public double? Min { get; init; } = 0d;

    [JsonPropertyName("max")]
    public double? Max { get; init; } = 0d;

    [JsonPropertyName("avg")]
    public double? Avg { get; init; } = 0d;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
