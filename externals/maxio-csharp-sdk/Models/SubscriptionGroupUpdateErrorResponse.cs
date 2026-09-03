using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionGroupUpdateErrorResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("errors")]
    public SubscriptionGroupUpdateError? Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
