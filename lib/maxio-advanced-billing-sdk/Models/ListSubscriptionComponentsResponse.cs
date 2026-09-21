using System.Collections.Generic;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record ListSubscriptionComponentsResponse
{
    [JsonPropertyName("subscriptions_components")]
    public required IReadOnlyList<SubscriptionComponent> SubscriptionsComponents { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
