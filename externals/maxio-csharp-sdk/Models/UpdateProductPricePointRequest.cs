using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UpdateProductPricePointRequest
{
    [JsonPropertyName("price_point")]
    public required UpdateProductPricePoint PricePoint { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
