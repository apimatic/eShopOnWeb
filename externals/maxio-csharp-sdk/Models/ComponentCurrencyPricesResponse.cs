using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ComponentCurrencyPricesResponse
{
    [JsonPropertyName("currency_prices")]
    public required IReadOnlyList<ComponentCurrencyPrice> CurrencyPrices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
