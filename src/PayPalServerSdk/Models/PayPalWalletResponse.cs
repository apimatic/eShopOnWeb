using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The PayPal Wallet response.
/// </summary>
public record PayPalWalletResponse
{
    /// <summary>
    /// The internationalized email address. Note: Up to 64 characters are allowed before and 255 characters are allowed after the @ sign. However, the generally accepted maximum length for an email address is 254 characters. The pattern verifies that an unquoted @ sign exists.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("email_address")]
    [StringLength(254, MinimumLength = 3)]
    [RegularExpression("(?:[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+(?:\\.[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+)*|(?:[\\x01-\\x08\\x0b\\x0c\\x0e-\\x1f\\x21\\x23-\\x5b\\x5d-\\x7f]|\\[\\x01-\\x09\\x0b\\x0c\\x0e-\\x7f])*\")@(?:(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\\.)+[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?|\\[(?:(?:(2(5[0-5]|[0-4][0-9])|1[0-9][0-9]|[1-9]?[0-9]))\\.){3}(?:(2(5[0-5]|[0-4][0-9])|1[0-9][0-9]|[1-9]?[0-9])|[a-zA-Z0-9-]*[a-zA-Z0-9]:(?:[\\x01-\\x08\\x0b\\x0c\\x0e-\\x1f\\x21-\\x5a\\x53-\\x7f]|\\[\\x01-\\x09\\x0b\\x0c\\x0e-\\x7f])+)\\])")]
    public string? EmailAddress { get; init; }

    /// <summary>
    /// The PayPal payer ID, which is a masked version of the PayPal account number intended for use with third parties. The account number is reversibly encrypted and a proprietary variant of Base32 is used to encode the result.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_id")]
    [StringLength(13, MinimumLength = 13)]
    [RegularExpression("^[2-9A-HJ-NP-Z]{13}$")]
    public string? AccountId { get; init; }

    /// <summary>
    /// The account status indicates whether the buyer has verified the financial details associated with their PayPal account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_status")]
    public PayPalWalletAccountVerificationStatus? AccountStatus { get; init; }

    /// <summary>
    /// The name of the party.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    public Name? Name { get; init; }

    /// <summary>
    /// The phone type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_type")]
    public PhoneType? PhoneType { get; init; }

    /// <summary>
    /// The phone number in its canonical international <see href="https://www.itu.int/rec/T-REC-E.164/en">E.164 numbering plan format</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number")]
    public PhoneNumber? PhoneNumber { get; init; }

    /// <summary>
    /// The stand-alone date, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. To represent special legal values, such as a date of birth, you should use dates with no associated time or time-zone data. Whenever possible, use the standard <c>date_time</c> type. This regular expression does not validate all dates. For example, February 31 is valid and nothing is known about leap years.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("birth_date")]
    [StringLength(10, MinimumLength = 10)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])$")]
    public string? BirthDate { get; init; }

    /// <summary>
    /// The business name of the PayPal account holder (populated for business accounts only)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_name")]
    [StringLength(300, MinimumLength = 0)]
    [RegularExpression("^.*$")]
    public string? BusinessName { get; init; }

    /// <summary>
    /// The tax ID of the customer. The customer is also known as the payer. Both <c>tax_id</c> and <c>tax_id_type</c> are required.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_info")]
    public TaxInfo? TaxInfo { get; init; }

    /// <summary>
    /// The portable international postal address. Maps to <see href="https://github.com/googlei18n/libaddressinput/wiki/AddressValidationMetadata">AddressValidationMetadata</see> and HTML 5.1 <see href="https://www.w3.org/TR/html51/sec-forms.html#autofilling-form-controls-the-autocomplete-attribute">Autofilling form controls: the autocomplete attribute</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address")]
    public Address? Address { get; init; }

    /// <summary>
    /// Additional attributes associated with the use of a PayPal Wallet.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attributes")]
    public PayPalWalletAttributesResponse? Attributes { get; init; }

    /// <summary>
    /// Provides additional details to process a payment using the PayPal wallet billing agreement or a vaulted payment method that has been stored or is intended to be stored.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stored_credential")]
    public PayPalWalletStoredCredential? StoredCredential { get; init; }

    /// <summary>
    /// This field indicates the status of PayPal's Checkout experience throughout the order lifecycle. The values reflect the current stage of the checkout process.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("experience_status")]
    public ExperienceStatus? ExperienceStatus { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
