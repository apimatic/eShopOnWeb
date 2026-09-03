using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UpdateSubscriptionGroup
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("member_ids")]
    public IReadOnlyList<int>? MemberIds { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
