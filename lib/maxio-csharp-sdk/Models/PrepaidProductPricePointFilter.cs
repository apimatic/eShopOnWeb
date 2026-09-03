using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record PrepaidProductPricePointFilter
{
    /// <summary>
    /// Passed as a parameter to list methods to return only non null values.
    /// </summary>
    [JsonPropertyName("product_price_point_id")]
    public required IncludeNotNull ProductPricePointId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
