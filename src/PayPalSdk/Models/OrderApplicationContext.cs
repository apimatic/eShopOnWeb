using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation;
using PayPal.Core.Validation.Attributes;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Customizes the payer experience during the approval process for the payment with PayPal. Note: Partners and Marketplaces might configure brand_name and shipping_preference during partner account setup, which overrides the request values.
/// </summary>
public record OrderApplicationContext
{
    /// <summary>
    /// DEPRECATED. The label that overrides the business name in the PayPal account on the PayPal site. The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.brand_name</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_name")]
    [StringLength(127, MinimumLength = 1)]
    public string? BrandName { get; init; }

    /// <summary>
    /// DEPRECATED. The BCP 47-formatted locale of pages that the PayPal payment experience shows. PayPal supports a five-character code. For example, <c>da-DK</c>, <c>he-IL</c>, <c>id-ID</c>, <c>ja-JP</c>, <c>no-NO</c>, <c>pt-BR</c>, <c>ru-RU</c>, <c>sv-SE</c>, <c>th-TH</c>, <c>zh-CN</c>, <c>zh-HK</c>, or <c>zh-TW</c>.  The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.locale</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("locale")]
    [StringLength(10, MinimumLength = 2)]
    [RegularExpression("^[a-z]{2}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|[0-9]{3}))?$")]
    public string? Locale { get; init; }

    /// <summary>
    /// DEPRECATED. DEPRECATED. The type of landing page to show on the PayPal site for customer checkout.  The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.landing_page</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
    /// </summary>
    [JsonPropertyName("landing_page")]
    public OrderApplicationContextLandingPage? LandingPage { get; init; } = OrderApplicationContextLandingPage.NoPreference;

    /// <summary>
    /// DEPRECATED. DEPRECATED. The shipping preference: Displays the shipping address to the customer. Enables the customer to choose an address on the PayPal site. Restricts the customer from changing the address during the payment-approval process. .  The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.shipping_preference</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
    /// </summary>
    [JsonPropertyName("shipping_preference")]
    public OrderApplicationContextShippingPreference? ShippingPreference { get; init; } = OrderApplicationContextShippingPreference.GetFromFile;

    /// <summary>
    /// DEPRECATED. Configures a Continue or Pay Now checkout flow.  The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.user_action</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
    /// </summary>
    [JsonPropertyName("user_action")]
    public OrderApplicationContextUserAction? UserAction { get; init; } = OrderApplicationContextUserAction.Continue;

    /// <summary>
    /// DEPRECATED. The customer and merchant payment preferences. The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.payment_method_selected</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object..
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_method")]
    public PaymentMethodPreference? PaymentMethod { get; init; }

    /// <summary>
    /// DEPRECATED. The URL where the customer is redirected after the customer approves the payment. The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.return_url</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("return_url")]
    [Format(FormatKind.Uri)]
    public string? ReturnUrl { get; init; }

    /// <summary>
    /// DEPRECATED. The URL where the customer is redirected after the customer cancels the payment. The fields in <c>application_context</c> are now available in the <c>experience_context</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.paypal.experience_context.cancel_url</c>). Please specify this field in the <c>experience_context</c> object instead of the <c>application_context</c> object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancel_url")]
    [Format(FormatKind.Uri)]
    public string? CancelUrl { get; init; }

    /// <summary>
    /// DEPRECATED. Provides additional details to process a payment using a <c>payment_source</c> that has been stored or is intended to be stored (also referred to as stored_credential or card-on-file). Parameter compatibility: <c>payment_type=ONE_TIME</c> is compatible only with <c>payment_initiator=CUSTOMER</c>. <c>usage=FIRST</c> is compatible only with <c>payment_initiator=CUSTOMER</c>. <c>previous_transaction_reference</c> or <c>previous_network_transaction_reference</c> is compatible only with <c>payment_initiator=MERCHANT</c>. Only one of the parameters - <c>previous_transaction_reference</c> and <c>previous_network_transaction_reference</c> - can be present in the request. .  The fields in <c>stored_payment_source</c> are now available in the <c>stored_credential</c> object under the <c>payment_source</c> which supports them (eg. <c>payment_source.card.stored_credential.payment_initiator</c>). Please specify this field in the <c>payment_source</c> object instead of the <c>application_context</c> object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stored_payment_source")]
    public StoredPaymentSource? StoredPaymentSource { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
