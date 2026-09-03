using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ListSubscriptionGroupPrepayment
{
    [JsonPropertyName("prepayment")]
    public required ListSubscriptionGroupPrepaymentItem Prepayment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
