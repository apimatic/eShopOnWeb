using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;

namespace Maxio.Models;

public record ComponentAllocationChange
{
    [JsonPropertyName("previous_allocation")]
    public required int PreviousAllocation { get; init; }

    [JsonPropertyName("new_allocation")]
    public required int NewAllocation { get; init; }

    [JsonPropertyName("component_id")]
    public required int ComponentId { get; init; }

    [JsonPropertyName("component_handle")]
    public required string ComponentHandle { get; init; }

    [JsonPropertyName("memo")]
    public required string Memo { get; init; }

    [JsonPropertyName("allocation_id")]
    public required int AllocationId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allocated_quantity")]
    public AllocatedQuantity? AllocatedQuantity { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
