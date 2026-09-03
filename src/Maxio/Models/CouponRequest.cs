using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CouponRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon")]
    public CouponPayload? Coupon { get; init; }

    /// <summary>
    /// An object where the keys are product IDs or handles (prefixed with 'handle:'), and the values are booleans indicating if the coupon should be applicable to the product.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("restricted_products")]
    public IReadOnlyDictionary<string, bool>? RestrictedProducts { get; init; }

    /// <summary>
    /// An object where the keys are component IDs or handles (prefixed with 'handle:'), and the values are booleans indicating if the coupon should be applicable to the component.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("restricted_components")]
    public IReadOnlyDictionary<string, bool>? RestrictedComponents { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
