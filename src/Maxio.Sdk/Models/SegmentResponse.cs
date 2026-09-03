using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SegmentResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segment")]
    public Segment? Segment { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
