using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record AllocationPreviewResponse
{
    [JsonPropertyName("allocation_preview")]
    public required AllocationPreview AllocationPreview { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
