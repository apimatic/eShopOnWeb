using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// Information used to pay Bancontact.
/// </summary>
public record BancontactPaymentObject
{
    /// <summary>
    /// The full name representation like Mr J Smith.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    [StringLength(300, MinimumLength = 3)]
    public string? Name { get; init; }

    /// <summary>
    /// The <see href="https://developer.paypal.com/api/rest/reference/country-codes/">two-character ISO 3166-1 code</see> that identifies the country or region. Note: The country code for Great Britain is GB and not UK as used in the top-level domain names for that country. Use the <c>C2</c> country code for China worldwide for comparable uncontrolled price (CUP) method, bank card, and cross-border transactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("country_code")]
    [StringLength(2, MinimumLength = 2)]
    [RegularExpression("^([A-Z]{2}|C2)$")]
    public string? CountryCode { get; init; }

    /// <summary>
    /// The business identification code (BIC). In payments systems, a BIC is used to identify a specific business, most commonly a bank.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bic")]
    [StringLength(11, MinimumLength = 8)]
    [RegularExpression("^[A-Z-a-z0-9]{4}[A-Z-a-z]{2}[A-Z-a-z0-9]{2}([A-Z-a-z0-9]{3})?$")]
    public string? Bic { get; init; }

    /// <summary>
    /// The last characters of the IBAN used to pay.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("iban_last_chars")]
    [StringLength(34, MinimumLength = 4)]
    [RegularExpression("[a-zA-Z0-9]{4}")]
    public string? IbanLastChars { get; init; }

    /// <summary>
    /// The last digits of the card used to fund the Bancontact payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("card_last_digits")]
    [StringLength(4, MinimumLength = 4)]
    [RegularExpression("[0-9]{4}")]
    public string? CardLastDigits { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
