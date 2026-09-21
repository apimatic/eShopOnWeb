using System.Collections.Generic;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record ListSubscriptionGroupPrepaymentResponse
{
    [JsonPropertyName("prepayments")]
    public required IReadOnlyList<ListSubscriptionGroupPrepayment> Prepayments { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
