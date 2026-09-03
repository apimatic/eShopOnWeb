using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UpdateCurrencyPrice
{
    /// <summary>
    /// ID of the currency price record being updated
    /// </summary>
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    /// <summary>
    /// New price for the given currency
    /// </summary>
    [JsonPropertyName("price")]
    public required double Price { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
