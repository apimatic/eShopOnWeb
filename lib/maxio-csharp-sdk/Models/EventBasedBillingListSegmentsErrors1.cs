using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record EventBasedBillingListSegmentsErrors1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("errors")]
    public ErrorsModel? Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
