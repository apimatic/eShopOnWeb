using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ComponentPricePointResponse
{
    [JsonPropertyName("price_point")]
    public required ComponentPricePoint PricePoint { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
