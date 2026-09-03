using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ComponentPricePointCurrencyOverageResponse
{
    /// <summary>
    /// Extends a component price point with currency overage prices.
    /// </summary>
    [JsonPropertyName("price_point")]
    public required CurrencyOveragePrices PricePoint { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
