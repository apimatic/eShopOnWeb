using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CloneComponentPricePointRequest
{
    [JsonPropertyName("price_point")]
    public required CloneComponentPricePoint PricePoint { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
