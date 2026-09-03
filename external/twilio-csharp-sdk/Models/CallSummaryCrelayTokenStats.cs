using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record CallSummaryCrelayTokenStats
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total")]
    public int? Total { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tokens_per_second")]
    public CallSummaryCrelayRateStats? TokensPerSecond { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
