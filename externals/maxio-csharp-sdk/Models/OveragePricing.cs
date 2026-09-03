using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record OveragePricing
{
    /// <summary>
    /// The identifier for the pricing scheme. See <see href="https://help.chargify.com/products/product-components.html">Product Components</see> for an overview of pricing schemes.
    /// </summary>
    [JsonPropertyName("pricing_scheme")]
    public required PricingScheme PricingScheme { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prices")]
    public IReadOnlyList<Price>? Prices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
