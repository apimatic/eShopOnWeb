using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

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
