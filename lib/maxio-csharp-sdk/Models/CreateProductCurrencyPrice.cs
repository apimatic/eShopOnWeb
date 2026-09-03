using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateProductCurrencyPrice
{
    /// <summary>
    /// ISO code for one of the site level currencies.
    /// </summary>
    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    /// <summary>
    /// Price for the given role.
    /// </summary>
    [JsonPropertyName("price")]
    public required int Price { get; init; }

    /// <summary>
    /// Role for the price.
    /// </summary>
    [JsonPropertyName("role")]
    public required CurrencyPriceRole Role { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
