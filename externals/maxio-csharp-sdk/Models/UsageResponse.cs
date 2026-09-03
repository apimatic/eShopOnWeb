using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UsageResponse
{
    [JsonPropertyName("usage")]
    public required Usage Usage { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
