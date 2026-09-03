using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateProductPricePointRequest
{
    [JsonPropertyName("price_point")]
    public required CreateProductPricePoint PricePoint { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
