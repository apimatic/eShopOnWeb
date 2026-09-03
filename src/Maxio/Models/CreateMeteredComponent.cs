using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateMeteredComponent
{
    [JsonPropertyName("metered_component")]
    public required MeteredComponent MeteredComponent { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
