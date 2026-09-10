using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

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
