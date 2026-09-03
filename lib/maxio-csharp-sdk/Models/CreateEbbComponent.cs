using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateEbbComponent
{
    [JsonPropertyName("event_based_component")]
    public required EbbComponent EventBasedComponent { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
