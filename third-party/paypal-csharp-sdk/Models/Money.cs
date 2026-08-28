using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// The currency and amount for a financial transaction, such as a balance or payment due.
/// </summary>
public record Money
{
    /// <summary>
    /// The <see href="https://developer.paypal.com/api/rest/reference/currency-codes/">three-character ISO-4217 currency code</see> that identifies the currency.
    /// </summary>
    [JsonPropertyName("currency_code")]
    [StringLength(3, MinimumLength = 3)]
    public required string CurrencyCode { get; init; }

    /// <summary>
    /// The value, which might be: An integer for currencies like <c>JPY</c> that are not typically fractional. A decimal fraction for currencies like <c>TND</c> that are subdivided into thousandths. For the required number of decimal places for a currency code, see <see href="https://developer.paypal.com/api/rest/reference/currency-codes/">Currency Codes</see>.
    /// </summary>
    [JsonPropertyName("value")]
    [MaxLength(32)]
    [RegularExpression("^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$")]
    public required string Value { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
