using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ProductResponse
{
    [JsonPropertyName("product")]
    public required Product Product { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
