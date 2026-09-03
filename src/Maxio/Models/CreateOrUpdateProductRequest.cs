using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateOrUpdateProductRequest
{
    [JsonPropertyName("product")]
    public required CreateOrUpdateProduct Product { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
