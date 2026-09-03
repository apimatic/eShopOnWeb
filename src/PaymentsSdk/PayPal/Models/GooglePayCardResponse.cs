using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// The payment card to use to fund a Google Pay payment response. Can be a credit or debit card.
/// </summary>
public record GooglePayCardResponse
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
    /// The last digits of the payment card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_digits")]
    [StringLength(4, MinimumLength = 2)]
    [RegularExpression("^[0-9]{2,4}$")]
    public string? LastDigits { get; init; }

    /// <summary>
    /// Type of card. i.e Credit, Debit and so on.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public CardType? Type { get; init; }

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
    /// Results of Authentication such as 3D Secure.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authentication_result")]
    public AuthenticationResponse? AuthenticationResult { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
