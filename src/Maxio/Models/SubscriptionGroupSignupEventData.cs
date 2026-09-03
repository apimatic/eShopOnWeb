using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionGroupSignupEventData
{
    [JsonPropertyName("subscription_group")]
    public required SubscriptionGroupSignupFailureData SubscriptionGroup { get; init; }

    [JsonPropertyName("customer")]
    public required Customer? Customer { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
