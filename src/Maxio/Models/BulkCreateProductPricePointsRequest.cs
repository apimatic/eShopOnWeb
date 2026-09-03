using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record BulkCreateProductPricePointsRequest
{
    [JsonPropertyName("price_points")]
    public required IReadOnlyList<CreateProductPricePoint> PricePoints { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
