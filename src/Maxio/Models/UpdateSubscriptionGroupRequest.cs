using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UpdateSubscriptionGroupRequest
{
    [JsonPropertyName("subscription_group")]
    public required UpdateSubscriptionGroup SubscriptionGroup { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
