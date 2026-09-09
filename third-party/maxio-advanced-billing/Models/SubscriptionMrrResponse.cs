using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Core.Validation.Attributes;

namespace MaxioAdvancedBilling.Models;

public record SubscriptionMrrResponse
{
    [JsonPropertyName("subscriptions_mrr")]
    [MinLength(1)]
    [UniqueItems]
    public required IReadOnlyList<SubscriptionMrr> SubscriptionsMrr { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
