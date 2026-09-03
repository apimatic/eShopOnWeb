using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CancelGroupedSubscriptionsRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("charge_unbilled_usage")]
    public bool? ChargeUnbilledUsage { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
