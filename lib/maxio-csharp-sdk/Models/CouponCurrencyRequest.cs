using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CouponCurrencyRequest
{
    [JsonPropertyName("currency_prices")]
    public required IReadOnlyList<UpdateCouponCurrency> CurrencyPrices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
