using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateProductFamilyRequest
{
    [JsonPropertyName("product_family")]
    public required CreateProductFamily ProductFamily { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
