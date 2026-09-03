using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateProductCurrencyPricesRequest
{
    [JsonPropertyName("currency_prices")]
    public required IReadOnlyList<CreateProductCurrencyPrice> CurrencyPrices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
