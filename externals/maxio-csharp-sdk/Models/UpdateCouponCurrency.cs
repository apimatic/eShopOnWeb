using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UpdateCouponCurrency
{
    /// <summary>
    /// ISO code for the site defined currency.
    /// </summary>
    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    /// <summary>
    /// Price for the given currency.
    /// </summary>
    [JsonPropertyName("price")]
    public required int Price { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
