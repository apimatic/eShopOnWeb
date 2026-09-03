using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CouponResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon")]
    public Coupon? Coupon { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
