using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Core.Validation.Attributes;

namespace Maxio.Models;

public record SubscriptionMrrResponse
{
    [JsonPropertyName("subscriptions_mrr")]
    [MinLength(1)]
    [UniqueItems]
    public required IReadOnlyList<SubscriptionMrr> SubscriptionsMrr { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
