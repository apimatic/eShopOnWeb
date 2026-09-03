using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record BulkUpdateSegmentsItem
{
    /// <summary>
    /// The ID of the segment you want to update.
    /// </summary>
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    /// <summary>
    /// The identifier for the pricing scheme. See <see href="https://help.chargify.com/products/product-components.html">Product Components</see> for an overview of pricing schemes.
    /// </summary>
    [JsonPropertyName("pricing_scheme")]
    public required PricingScheme PricingScheme { get; init; }

    [JsonPropertyName("prices")]
    public required IReadOnlyList<CreateOrUpdateSegmentPrice> Prices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
