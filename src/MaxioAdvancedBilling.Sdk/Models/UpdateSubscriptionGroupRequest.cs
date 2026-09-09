using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record UpdateSubscriptionGroupRequest
{
    [JsonPropertyName("subscription_group")]
    public required UpdateSubscriptionGroup SubscriptionGroup { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
