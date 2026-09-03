using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record EventBasedBillingSegment1
{
    [JsonPropertyName("errors")]
    public required EventBasedBillingSegmentError Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
