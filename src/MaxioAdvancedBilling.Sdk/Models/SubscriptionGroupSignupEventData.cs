using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record SubscriptionGroupSignupEventData
{
    [JsonPropertyName("subscription_group")]
    public required SubscriptionGroupSignupFailureData SubscriptionGroup { get; init; }

    [JsonPropertyName("customer")]
    public required Customer? Customer { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
