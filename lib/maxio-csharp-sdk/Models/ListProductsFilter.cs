using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ListProductsFilter
{
    /// <summary>
    /// Allows fetching products with matching id based on provided values. Use in query <c>filter[ids]=1,2,3</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ids")]
    [MinLength(1)]
    public IReadOnlyList<int>? Ids { get; init; }

    /// <summary>
    /// Allows fetching products only if a prepaid product price point is present or not. To use this filter you also have to include the following param in the request <c>include=prepaid_product_price_point</c>. Use in query <c>filter[prepaid_product_price_point][product_price_point_id]=not_null</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prepaid_product_price_point")]
    public PrepaidProductPricePointFilter? PrepaidProductPricePoint { get; init; }

    /// <summary>
    /// Allows fetching products with matching use_site_exchange_rate based on provided value (refers to default price point). Use in query <c>filter[use_site_exchange_rate]=true</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_site_exchange_rate")]
    public bool? UseSiteExchangeRate { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
