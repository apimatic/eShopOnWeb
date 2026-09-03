using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ListSubscriptionComponentsResponse
{
    [JsonPropertyName("subscriptions_components")]
    public required IReadOnlyList<SubscriptionComponent> SubscriptionsComponents { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
