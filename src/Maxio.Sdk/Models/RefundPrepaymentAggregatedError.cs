using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record RefundPrepaymentAggregatedError
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("refund")]
    public PrepaymentAggregatedError? Refund { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
