using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation;
using PayPal.Core.Validation.Attributes;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Customizes the payer experience during the approval process for payment with PayPal. Note: Partners and Marketplaces might configure brand_name and shipping_preference during partner account setup, which overrides the request values.
/// </summary>
public record PayPalWalletExperienceContext
{
    /// <summary>
    /// The label that overrides the business name in the PayPal account on the PayPal site. The pattern is defined by an external party and supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_name")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? BrandName { get; init; }

    /// <summary>
    /// The <see href="https://tools.ietf.org/html/bcp47#section-2">language tag</see> for the language in which to localize the error-related strings, such as messages, issues, and suggested actions. The tag is made up of the <see href="https://www.loc.gov/standards/iso639-2/php/code_list.php">ISO 639-2 language code</see>, the optional <see href="https://www.unicode.org/iso15924/codelists.html">ISO-15924 script tag</see>, and the <see href="https://developer.paypal.com/api/rest/reference/country-codes/">ISO-3166 alpha-2 country code</see> or <see href="https://unstats.un.org/unsd/methodology/m49/">M49 region code</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("locale")]
    [StringLength(10, MinimumLength = 2)]
    [RegularExpression("^[a-z]{2}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|[0-9]{3}))?$")]
    public string? Locale { get; init; }

    /// <summary>
    /// The location from which the shipping address is derived.
    /// </summary>
    [JsonPropertyName("shipping_preference")]
    public PayPalWalletContextShippingPreference? ShippingPreference { get; init; } = PayPalWalletContextShippingPreference.GetFromFile;

    /// <summary>
    /// The preference to display the contact information (buyer’s shipping email &amp; phone number) on PayPal's checkout for easy merchant-buyer communication.
    /// </summary>
    [JsonPropertyName("contact_preference")]
    public PayPalWalletContactPreference? ContactPreference { get; init; } = PayPalWalletContactPreference.NoContactInfo;

    /// <summary>
    /// Describes the URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("return_url")]
    [Format(FormatKind.Uri)]
    public string? ReturnUrl { get; init; }

    /// <summary>
    /// Describes the URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancel_url")]
    [Format(FormatKind.Uri)]
    public string? CancelUrl { get; init; }

    /// <summary>
    /// Merchant provided details of the native app or mobile web browser to facilitate buyer's app switch to the PayPal consumer app.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("app_switch_context")]
    public AppSwitchContext? AppSwitchContext { get; init; }

    /// <summary>
    /// The type of landing page to show on the PayPal site for customer checkout.
    /// </summary>
    [JsonPropertyName("landing_page")]
    public PayPalExperienceLandingPage? LandingPage { get; init; } = PayPalExperienceLandingPage.NoPreference;

    /// <summary>
    /// Configures a Continue or Pay Now checkout flow.
    /// </summary>
    [JsonPropertyName("user_action")]
    public PayPalExperienceUserAction? UserAction { get; init; } = PayPalExperienceUserAction.Continue;

    /// <summary>
    /// The merchant-preferred payment methods.
    /// </summary>
    [JsonPropertyName("payment_method_preference")]
    public PayeePaymentMethodPreference? PaymentMethodPreference { get; init; } = PayeePaymentMethodPreference.Unrestricted;

    /// <summary>
    /// CallBack Configuration that the merchant can provide to PayPal/Venmo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("order_update_callback_config")]
    public CallbackConfiguration? OrderUpdateCallbackConfig { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
