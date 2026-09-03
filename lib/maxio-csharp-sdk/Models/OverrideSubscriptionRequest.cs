using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record OverrideSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required OverrideSubscription Subscription { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
