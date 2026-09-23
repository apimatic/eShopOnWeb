using System.Collections.Generic;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record CurrencyPricesResponse
{
    [JsonPropertyName("currency_prices")]
    public required IReadOnlyList<CurrencyPrice> CurrencyPrices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
