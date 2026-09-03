using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreatePrepaidComponent
{
    [JsonPropertyName("prepaid_usage_component")]
    public required PrepaidUsageComponent PrepaidUsageComponent { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
