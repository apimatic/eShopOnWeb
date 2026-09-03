using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record EventResponse
{
    [JsonPropertyName("event")]
    public required Event Event { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
