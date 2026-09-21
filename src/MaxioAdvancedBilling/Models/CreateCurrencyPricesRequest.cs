using System.Collections.Generic;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record CreateCurrencyPricesRequest
{
    [JsonPropertyName("currency_prices")]
    public required IReadOnlyList<CreateCurrencyPrice> CurrencyPrices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
