using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateOnOffComponent
{
    [JsonPropertyName("on_off_component")]
    public required OnOffComponent OnOffComponent { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
