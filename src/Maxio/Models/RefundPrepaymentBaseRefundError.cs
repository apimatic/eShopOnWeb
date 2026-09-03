using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record RefundPrepaymentBaseRefundError
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("refund")]
    public BaseRefundError? Refund { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
