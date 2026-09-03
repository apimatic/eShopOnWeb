using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UpdateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required UpdateSubscription Subscription { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
