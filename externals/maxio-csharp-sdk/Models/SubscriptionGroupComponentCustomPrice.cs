using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Used in place of <c>price_point_id</c> to define a custom price point unique to the subscription. You still need to provide <c>component_id</c>.
/// </summary>
public record SubscriptionGroupComponentCustomPrice
{
    /// <summary>
    /// The identifier for the pricing scheme. See <see href="https://help.chargify.com/products/product-components.html">Product Components</see> for an overview of pricing schemes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pricing_scheme")]
    public PricingScheme? PricingScheme { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prices")]
    public IReadOnlyList<Price>? Prices { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("overage_pricing")]
    public IReadOnlyList<ComponentCustomPrice>? OveragePricing { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
