using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record PrepaidUsage
{
    [JsonPropertyName("previous_unit_balance")]
    [MinLength(1)]
    public required string PreviousUnitBalance { get; init; }

    [JsonPropertyName("previous_overage_unit_balance")]
    [MinLength(1)]
    public required string PreviousOverageUnitBalance { get; init; }

    [JsonPropertyName("new_unit_balance")]
    public required int NewUnitBalance { get; init; }

    [JsonPropertyName("new_overage_unit_balance")]
    public required int NewOverageUnitBalance { get; init; }

    [JsonPropertyName("usage_quantity")]
    public required int UsageQuantity { get; init; }

    [JsonPropertyName("overage_usage_quantity")]
    public required int OverageUsageQuantity { get; init; }

    [JsonPropertyName("component_id")]
    public required int ComponentId { get; init; }

    [JsonPropertyName("component_handle")]
    public required string ComponentHandle { get; init; }

    [JsonPropertyName("memo")]
    public required string Memo { get; init; }

    [JsonPropertyName("allocation_details")]
    public required IReadOnlyList<PrepaidUsageAllocationDetail> AllocationDetails { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
