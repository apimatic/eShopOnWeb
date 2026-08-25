using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The total order amount with an optional breakdown that provides details, such as the total item amount, total tax amount, shipping, handling, insurance, and discounts, if any. If you specify <c>amount.breakdown</c>, the amount equals <c>item_total</c> plus <c>tax_total</c> plus <c>shipping</c> plus <c>handling</c> plus <c>insurance</c> minus <c>shipping_discount</c> minus discount. The amount must be a positive number. For listed of supported currencies and decimal precision, see the PayPal REST APIs Currency Codes.
/// </summary>
public record AmountWithBreakdown
{
    /// <summary>
    /// The <see href="/api/rest/reference/currency-codes/">three-character ISO-4217 currency code</see> that identifies the currency.
    /// </summary>
    [JsonPropertyName("currency_code")]
    [StringLength(3, MinimumLength = 3)]
    public required string CurrencyCode { get; init; }

    /// <summary>
    /// The value, which might be: An integer for currencies like <c>JPY</c> that are not typically fractional. A decimal fraction for currencies like <c>TND</c> that are subdivided into thousandths. For the required number of decimal places for a currency code, see <see href="/api/rest/reference/currency-codes/">Currency Codes</see>.
    /// </summary>
    [JsonPropertyName("value")]
    [MaxLength(32)]
    [RegularExpression("^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$")]
    public required string Value { get; init; }

    /// <summary>
    /// The breakdown of the amount. Breakdown provides details such as total item amount, total tax amount, shipping, handling, insurance, and discounts, if any.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("breakdown")]
    public AmountBreakdown? Breakdown { get; init; }
}
