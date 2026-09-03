using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UpdateComponentRequest
{
    [JsonPropertyName("component")]
    public required UpdateComponent Component { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
