using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record CreateSubscriptionGroupRequest
{
    [JsonPropertyName("subscription_group")]
    public required CreateSubscriptionGroup SubscriptionGroup { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
