using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment card used to fund the payment. Card can be a credit or debit card.
/// </summary>
public record CardResponseWithBillingAddress
{
    /// <summary>
    /// The card holder's name as it appears on the card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    [StringLength(300, MinimumLength = 2)]
    public string? Name { get; init; }

    /// <summary>
    /// The portable international postal address. Maps to <see href="https://github.com/googlei18n/libaddressinput/wiki/AddressValidationMetadata">AddressValidationMetadata</see> and HTML 5.1 <see href="https://www.w3.org/TR/html51/sec-forms.html#autofilling-form-controls-the-autocomplete-attribute">Autofilling form controls: the autocomplete attribute</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address")]
    public Address? BillingAddress { get; init; }

    /// <summary>
    /// The year and month, in ISO-8601 <c>YYYY-MM</c> date format. See <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiry")]
    [StringLength(7, MinimumLength = 7)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    public string? Expiry { get; init; }

    /// <summary>
    /// The <see href="/api/rest/reference/currency-codes/">three-character ISO-4217 currency code</see> that identifies the currency.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currency_code")]
    [StringLength(3, MinimumLength = 3)]
    public string? CurrencyCode { get; init; }
}
