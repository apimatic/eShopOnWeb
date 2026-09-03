using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record AddSubscriptionToAGroup
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("group")]
    public GroupSettings? Group { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
