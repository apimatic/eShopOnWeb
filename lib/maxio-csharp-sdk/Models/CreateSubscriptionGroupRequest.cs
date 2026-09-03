using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateSubscriptionGroupRequest
{
    [JsonPropertyName("subscription_group")]
    public required CreateSubscriptionGroup SubscriptionGroup { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
