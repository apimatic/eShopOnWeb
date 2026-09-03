using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ListSubscriptionGroupPrepaymentResponse
{
    [JsonPropertyName("prepayments")]
    public required IReadOnlyList<ListSubscriptionGroupPrepayment> Prepayments { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
