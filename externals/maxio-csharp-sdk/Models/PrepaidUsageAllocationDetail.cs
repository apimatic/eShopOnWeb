using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record PrepaidUsageAllocationDetail
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allocation_id")]
    public int? AllocationId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("charge_id")]
    public int? ChargeId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage_quantity")]
    public int? UsageQuantity { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
