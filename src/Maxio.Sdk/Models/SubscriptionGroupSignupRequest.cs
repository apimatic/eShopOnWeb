using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionGroupSignupRequest
{
    [JsonPropertyName("subscription_group")]
    public required SubscriptionGroupSignup SubscriptionGroup { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
