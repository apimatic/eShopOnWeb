using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// A Resource representing a request to vault a Card.
/// </summary>
public record SetupTokenRequestCard
{
    /// <summary>
    /// The card holder's name as it appears on the card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    [StringLength(300, MinimumLength = 1)]
    [RegularExpression("^.{1,300}$")]
    public string? Name { get; init; }

    /// <summary>
    /// The primary account number (PAN) for the payment card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("number")]
    [StringLength(19, MinimumLength = 13)]
    [RegularExpression("^[0-9]{13,19}$")]
    public string? Number { get; init; }

    /// <summary>
    /// The year and month, in ISO-8601 <c>YYYY-MM</c> date format. See <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiry")]
    [StringLength(7, MinimumLength = 7)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    public string? Expiry { get; init; }

    /// <summary>
    /// The three- or four-digit security code of the card. Also known as the CVV, CVC, CVN, CVE, or CID. This parameter cannot be present in the request when <c>payment_initiator=MERCHANT</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("security_code")]
    [StringLength(4, MinimumLength = 3)]
    [RegularExpression("^[0-9]{3,4}$")]
    public string? SecurityCode { get; init; }

    /// <summary>
    /// The card network or brand. Applies to credit, debit, gift, and payment cards.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand")]
    public CardBrand? Brand { get; init; }

    /// <summary>
    /// The portable international postal address. Maps to <see href="https://github.com/googlei18n/libaddressinput/wiki/AddressValidationMetadata">AddressValidationMetadata</see> and HTML 5.1 <see href="https://www.w3.org/TR/html51/sec-forms.html#autofilling-form-controls-the-autocomplete-attribute">Autofilling form controls: the autocomplete attribute</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_address")]
    public Address? BillingAddress { get; init; }

    /// <summary>
    /// The verification method of the card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verification_method")]
    public VaultCardVerificationMethod? VerificationMethod { get; init; }

    /// <summary>
    /// A resource representing an experience context of vault a card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("experience_context")]
    public VaultCardExperienceContext? ExperienceContext { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
