using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Validation;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Models;

/// <summary>
/// Customizes the payer confirmation experience.
/// </summary>
public record OrderConfirmApplicationContext
{
    /// <summary>
    /// Label to present to your payer as part of the PayPal hosted web experience.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_name")]
    [StringLength(127, MinimumLength = 1)]
    public string? BrandName { get; init; }

    /// <summary>
    /// The <see href="https://tools.ietf.org/html/bcp47#section-2">language tag</see> for the language in which to localize the error-related strings, such as messages, issues, and suggested actions. The tag is made up of the <see href="https://www.loc.gov/standards/iso639-2/php/code_list.php">ISO 639-2 language code</see>, the optional <see href="https://www.unicode.org/iso15924/codelists.html">ISO-15924 script tag</see>, and the <see href="/api/rest/reference/country-codes/">ISO-3166 alpha-2 country code</see> or <see href="https://unstats.un.org/unsd/methodology/m49/">M49 region code</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("locale")]
    [StringLength(10, MinimumLength = 2)]
    [RegularExpression("^[a-z]{2}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|[0-9]{3}))?$")]
    public string? Locale { get; init; }

    /// <summary>
    /// The URL where the customer is redirected after the customer approves the payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("return_url")]
    [StringLength(4000, MinimumLength = 10)]
    [Format(FormatKind.Uri)]
    public string? ReturnUrl { get; init; }

    /// <summary>
    /// The URL where the customer is redirected after the customer cancels the payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancel_url")]
    [StringLength(4000, MinimumLength = 10)]
    [Format(FormatKind.Uri)]
    public string? CancelUrl { get; init; }

    /// <summary>
    /// Provides additional details to process a payment using a <c>payment_source</c> that has been stored or is intended to be stored (also referred to as stored_credential or card-on-file). Parameter compatibility: <c>payment_type=ONE_TIME</c> is compatible only with <c>payment_initiator=CUSTOMER</c>. <c>usage=FIRST</c> is compatible only with <c>payment_initiator=CUSTOMER</c>. <c>previous_transaction_reference</c> or <c>previous_network_transaction_reference</c> is compatible only with <c>payment_initiator=MERCHANT</c>. Only one of the parameters - <c>previous_transaction_reference</c> and <c>previous_network_transaction_reference</c> - can be present in the request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stored_payment_source")]
    public StoredPaymentSource? StoredPaymentSource { get; init; }
}
