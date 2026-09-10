using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record ListSubscriptionGroupPrepayment
{
    [JsonPropertyName("prepayment")]
    public required ListSubscriptionGroupPrepaymentItem Prepayment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
