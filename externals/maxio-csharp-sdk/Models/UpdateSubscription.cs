using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;

namespace Maxio.Models;

public record UpdateSubscription
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("credit_card_attributes")]
    public CreditCardAttributes? CreditCardAttributes { get; init; }

    /// <summary>
    /// Set to the handle of a different product to change the subscription's product.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; init; }

    /// <summary>
    /// Set to the id of a different product to change the subscription's product.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_id")]
    public int? ProductId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_change_delayed")]
    public bool? ProductChangeDelayed { get; init; }

    /// <summary>
    /// Set to an empty string to cancel a delayed product change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_product_id")]
    public string? NextProductId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_product_price_point_id")]
    public string? NextProductPricePointId { get; init; }

    /// <summary>
    /// A day of month that subscription will be processed on. Can be 1 up to 28 or 'end'.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("snap_day")]
    public SnapDay1? SnapDay { get; init; }

    /// <summary>
    /// (Optional) Set this attribute to a future date/time to update a subscription in the Awaiting Signup Date state, to Awaiting Signup. In the Awaiting Signup state, a subscription behaves like any other. It can be canceled, allocated to, or have its billing date changed, etc. When the <c>initial_billing_at</c> date hits, the subscription will transition to the expected state. If the product has a trial, the subscription will enter a trial, otherwise it will go active. Setup fees will be respected either before or after the trial, as configured on the price point. If the payment is due at the initial_billing_at and it fails the subscription will be immediately canceled. You can omit the initial_billing_at date to activate the subscription immediately. See the <see href="https://maxio.zendesk.com/hc/en-us/articles/24251489107213-Advanced-Billing-Subscription-Imports#date-format">subscription import</see> documentation for more information about Date/Time formats.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("initial_billing_at")]
    public DateTimeOffset? InitialBillingAt { get; init; }

    /// <summary>
    /// (Optional) Set this attribute to true to move the subscription from Awaiting Signup, to Awaiting Signup Date. Use this when you want to update a subscription that has an unknown initial billing date. When the first billing date is known, update a subscription to set the <c>initial_billing_at</c> date. The subscription moves to the awaiting signup with a scheduled initial billing date. You can omit the initial_billing_at date to activate the subscription immediately. See <see href="https://maxio-chargify.zendesk.com/hc/en-us/articles/5404222005773-Subscription-States">Subscription States</see> for more information.
    /// </summary>
    [JsonPropertyName("defer_signup")]
    public bool? DeferSignup { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_billing_at")]
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>
    /// The ID of the Branding Theme to assign to this subscription. When set, this subscription-level Branding Theme is used instead of the customer's default Branding Theme for subscription-related documents and communications that use subscription theming. Pass null or an empty value to clear the subscription-level Branding Theme. Available only when Branding Themes are enabled for the site. Not returned in the response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("branding_theme_id")]
    public int? BrandingThemeId { get; init; }

    /// <summary>
    /// Timestamp giving the expiration date of this subscription (if any). You may manually change the expiration date at any point during a subscription period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("receives_invoice_emails")]
    public bool? ReceivesInvoiceEmails { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("net_terms")]
    public NetTerms1? NetTerms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stored_credential_transaction_id")]
    public int? StoredCredentialTransactionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    /// <summary>
    /// (Optional) Used in place of <c>product_price_point_id</c> to define a custom price point unique to the subscription. A subscription can have up to 30 custom price points. Exceeding this limit will result in an API error.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_price")]
    public SubscriptionCustomPrice? CustomPrice { get; init; }

    /// <summary>
    /// (Optional) An array of component ids and custom prices to be added to the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("components")]
    public IReadOnlyList<UpdateSubscriptionComponent>? Components { get; init; }

    /// <summary>
    /// Enable Communication Delay feature, making sure no communication (email or SMS) is sent to the Customer between 9PM and 8AM in time zone set by the <c>dunning_communication_delay_time_zone</c> attribute.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dunning_communication_delay_enabled")]
    public bool? DunningCommunicationDelayEnabled { get; init; }

    /// <summary>
    /// Time zone for the Dunning Communication Delay feature.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dunning_communication_delay_time_zone")]
    public string? DunningCommunicationDelayTimeZone { get; init; }

    /// <summary>
    /// Set to change the current product's price point.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_id")]
    public int? ProductPricePointId { get; init; }

    /// <summary>
    /// Set to change the current product's price point.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
