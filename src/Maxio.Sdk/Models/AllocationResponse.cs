using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record AllocationResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allocation")]
    public Allocation? Allocation { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
