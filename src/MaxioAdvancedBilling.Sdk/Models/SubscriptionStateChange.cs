using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record SubscriptionStateChange
{
    [JsonPropertyName("previous_subscription_state")]
    [MinLength(1)]
    public required string PreviousSubscriptionState { get; init; }

    [JsonPropertyName("new_subscription_state")]
    [MinLength(1)]
    public required string NewSubscriptionState { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
