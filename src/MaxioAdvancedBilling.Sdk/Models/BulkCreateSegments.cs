using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record BulkCreateSegments
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segments")]
    [MaxLength(2000)]
    public IReadOnlyList<CreateSegment>? Segments { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
