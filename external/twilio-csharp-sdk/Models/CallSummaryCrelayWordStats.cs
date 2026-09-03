using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record CallSummaryCrelayWordStats
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total")]
    public int? Total { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("words_per_minute")]
    public CallSummaryCrelayRateStats? WordsPerMinute { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
