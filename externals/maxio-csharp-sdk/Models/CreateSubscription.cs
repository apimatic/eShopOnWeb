using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateSubscription
{
    /// <summary>
    /// The API Handle of the product for which you are creating a subscription. Required, unless a <c>product_id</c> is given instead.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; init; }

    /// <summary>
    /// The Product ID of the product for which you are creating a subscription. The product ID is not currently published, so we recommend using the API Handle instead.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_id")]
    public int? ProductId { get; init; }

    /// <summary>
    /// The user-friendly API handle of a product's particular price point.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; init; }

    /// <summary>
    /// The ID of the particular price point on the product.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_id")]
    public int? ProductPricePointId { get; init; }

    /// <summary>
    /// (Optional) Used in place of <c>product_price_point_id</c> to define a custom price point unique to the subscription. A subscription can have up to 30 custom price points. Exceeding this limit will result in an API error.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_price")]
    public SubscriptionCustomPrice? CustomPrice { get; init; }

    /// <summary>
    /// (deprecated) The coupon code of the single coupon currently applied to the subscription. See coupon_codes instead as subscriptions can now have more than one coupon.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon_code")]
    public string? CouponCode { get; init; }

    /// <summary>
    /// An array for all the coupons attached to the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon_codes")]
    public IReadOnlyList<string>? CouponCodes { get; init; }

    /// <summary>
    /// The type of payment collection to be used in the subscription. For legacy Statements Architecture valid options are - <c>invoice</c>, <c>automatic</c>. For current Relationship Invoicing Architecture valid options are - <c>remittance</c>, <c>automatic</c>, <c>prepaid</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_collection_method")]
    public CollectionMethod? PaymentCollectionMethod { get; init; }

    /// <summary>
    /// (Optional) Default: True - Whether or not this subscription is set to receive emails related to this subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("receives_invoice_emails")]
    public string? ReceivesInvoiceEmails { get; init; }

    /// <summary>
    /// (Optional) Default: null The number of days after renewal (on invoice billing) that a subscription is due. A value between 0 (due immediately) and 180.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("net_terms")]
    public string? NetTerms { get; init; }

    /// <summary>
    /// The ID of an existing customer within Chargify. Required, unless a <c>customer_reference</c> or a set of <c>customer_attributes</c> is given.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; init; }

    /// <summary>
    /// The ID of the Branding Theme to assign to this subscription. When set, this subscription-level Branding Theme is used instead of the customer's default Branding Theme for subscription-related documents and communications that use subscription theming. Pass null or an empty value to clear the subscription-level Branding Theme. Available only when Branding Themes are enabled for the site. Not returned in the response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("branding_theme_id")]
    public int? BrandingThemeId { get; init; }

    /// <summary>
    /// (Optional) Set this attribute to a future date/time to sync imported subscriptions to your existing renewal schedule. See the notes on “Date/Time Format” in our <see href="https://maxio.zendesk.com/hc/en-us/articles/24251489107213-Advanced-Billing-Subscription-Imports#date-format">subscription import documentation</see>. If you provide a next_billing_at timestamp that is in the future, no trial or initial charges will be applied when you create the subscription. In fact, no payment will be captured at all. The first payment will be captured, according to the prices defined by the product, near the time specified by next_billing_at. If you do not provide a value for next_billing_at, any trial and/or initial charges will be assessed and charged at the time of subscription creation. If the card cannot be successfully charged, the subscription will not be created. See further notes in the section on Importing Subscriptions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_billing_at")]
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>
    /// (Optional) Set this attribute to a future date/time to create a subscription in the Awaiting Signup state, rather than Active or Trialing. You can omit the initial_billing_at date to activate the subscription immediately. In the Awaiting Signup state, a subscription behaves like any other. It can be canceled, allocated to, or have its billing date changed. etc. When the initial_billing_at date hits, the subscription will transition to the expected state. If the product has a trial, the subscription will enter a trial, otherwise it will go active. Setup fees will be respected either before or after the trial, as configured on the price point. If the payment is due at the initial_billing_at and it fails the subscription will be immediately canceled. See the <see href="https://maxio.zendesk.com/hc/en-us/articles/24251489107213-Advanced-Billing-Subscription-Imports#date-format">subscription import</see> documentation for more information about Date/Time Formats.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("initial_billing_at")]
    public DateTimeOffset? InitialBillingAt { get; init; }

    /// <summary>
    /// (Optional) Set this attribute to true to create the subscription in the Awaiting Signup Date state. Use this when you want to create a subscription that has an unknown first billing date. When the first billing date is known, update a subscription and set the <c>initial_billing_at</c> date. The subscription moves to the Awaiting Signup state with a scheduled initial billing date. You can omit the initial_billing_at date to activate the subscription immediately. See <see href="https://maxio-chargify.zendesk.com/hc/en-us/articles/5404222005773-Subscription-States">Subscription States</see> for more information.
    /// </summary>
    [JsonPropertyName("defer_signup")]
    public bool? DeferSignup { get; init; } = false;

    /// <summary>
    /// For European sites subject to PSD2 and using 3D Secure, this can be used to reference a previous transaction for the customer. This will ensure the card will be charged successfully at renewal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stored_credential_transaction_id")]
    public int? StoredCredentialTransactionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sales_rep_id")]
    public int? SalesRepId { get; init; }

    /// <summary>
    /// The Payment Profile ID of an existing card or bank account, which belongs to an existing customer to use for payment for this subscription. If the card, bank account, or customer does not exist already, or if you want to use a new (unstored) card or bank account for the subscription, use <c>payment_profile_attributes</c> instead to create a new payment profile along with the subscription. (This value is available on an existing subscription via the API as <c>credit_card</c> &gt; id or <c>bank_account</c> &gt; id.)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_profile_id")]
    public int? PaymentProfileId { get; init; }

    /// <summary>
    /// The reference value (provided by your app) for the subscription itself.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_attributes")]
    public CustomerAttributes? CustomerAttributes { get; init; }

    /// <summary>
    /// alias to credit_card_attributes
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_profile_attributes")]
    public PaymentProfileAttributes? PaymentProfileAttributes { get; init; }

    /// <summary>
    /// Credit Card data to create a new Subscription. Interchangeable with <c>payment_profile_attributes</c> property.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("credit_card_attributes")]
    public PaymentProfileAttributes? CreditCardAttributes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_account_attributes")]
    public BankAccountAttributes? BankAccountAttributes { get; init; }

    /// <summary>
    /// (Optional) An array of component ids and quantities to be added to the subscription. See <see href="https://maxio.zendesk.com/hc/en-us/articles/24261141522189-Components-Overview">Components</see> for more information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("components")]
    public IReadOnlyList<CreateSubscriptionComponent>? Components { get; init; }

    /// <summary>
    /// (Optional). Cannot be used when also specifying next_billing_at.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("calendar_billing")]
    public CalendarBilling? CalendarBilling { get; init; }

    /// <summary>
    /// (Optional) A set of key/value pairs representing custom fields and their values. Metafields will be created “on-the-fly” in your site for a given key, if they have not been created yet.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metafields")]
    public IReadOnlyDictionary<string, string>? Metafields { get; init; }

    /// <summary>
    /// The reference value (provided by your app) of an existing customer within Chargify. Required, unless a <c>customer_id</c> or a set of <c>customer_attributes</c> is given.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("group")]
    public GroupSettings? Group { get; init; }

    /// <summary>
    /// A valid referral code. (optional, see <see href="https://maxio.zendesk.com/hc/en-us/articles/24286981223693-Referrals-Reference#how-to-obtain-referral-codes">Referrals</see> for more details). If supplied, must be valid, or else subscription creation will fail.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ref")]
    public string? Ref { get; init; }

    /// <summary>
    /// (Optional) Can be used when canceling a subscription (via the HTTP DELETE method) to make a note about the reason for cancellation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancellation_message")]
    public string? CancellationMessage { get; init; }

    /// <summary>
    /// (Optional) Can be used when canceling a subscription (via the HTTP DELETE method) to make a note about how the subscription was canceled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancellation_method")]
    public string? CancellationMethod { get; init; }

    /// <summary>
    /// (Optional) If Multi-Currency is enabled and the currency is configured in Chargify, pass it at signup to create a subscription on a non-default currency. Note that you cannot update the currency of an existing subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// Timestamp giving the expiration date of this subscription (if any). You may manually change the expiration date at any point during a subscription period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// (Optional, default false) When set to true, and when next_billing_at is present, if the subscription expires, the expires_at will be shifted by the same amount of time as the difference between the old and new “next billing” dates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_tracks_next_billing_change")]
    public string? ExpirationTracksNextBillingChange { get; init; }

    /// <summary>
    /// (Optional) The ACH authorization agreement terms. If enabled, an email will be sent to the customer with a copy of the terms.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("agreement_terms")]
    public string? AgreementTerms { get; init; }

    /// <summary>
    /// (Optional) The first name of the person authorizing the ACH agreement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authorizer_first_name")]
    public string? AuthorizerFirstName { get; init; }

    /// <summary>
    /// (Optional) The last name of the person authorizing the ACH agreement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authorizer_last_name")]
    public string? AuthorizerLastName { get; init; }

    /// <summary>
    /// (Optional) One of “prorated” (the default – the prorated product price will be charged immediately), “immediate” (the full product price will be charged immediately), or “delayed” (the full product price will be charged with the first scheduled renewal).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("calendar_billing_first_charge")]
    public string? CalendarBillingFirstCharge { get; init; }

    /// <summary>
    /// (Optional) Can be used when canceling a subscription (via the HTTP DELETE method) to indicate why a subscription was canceled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }

    /// <summary>
    /// (Optional) used only for Delayed Product Change When set to true, indicates that a changed value for product_handle should schedule the product change to the next subscription renewal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_change_delayed")]
    public bool? ProductChangeDelayed { get; init; }

    /// <summary>
    /// Use in place of passing product and component information to set up the subscription with an existing offer. May be either the Chargify id of the offer or its handle prefixed with <c>handle:</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("offer_id")]
    public OfferId? OfferId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prepaid_configuration")]
    public UpsertPrepaidConfiguration? PrepaidConfiguration { get; init; }

    /// <summary>
    /// Providing a previous_billing_at that is in the past will set the current_period_starts_at when the subscription is created. It will also set activated_at if not explicitly passed during the subscription import. Can only be used if next_billing_at is also passed. Using this option will allow you to set the period start for the subscription so mid period component allocations have the correct prorated amount.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("previous_billing_at")]
    public DateTimeOffset? PreviousBillingAt { get; init; }

    /// <summary>
    /// Setting this attribute to true will cause the subscription's MRR to be added to your MRR analytics immediately. For this value to be honored, a next_billing_at must be present and set to a future date. This key/value will not be returned in the subscription response body.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("import_mrr")]
    public bool? ImportMrr { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; init; }

    /// <summary>
    /// Required when creating a subscription with Maxio Payments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("agreement_acceptance")]
    public AgreementAcceptance? AgreementAcceptance { get; init; }

    /// <summary>
    /// (Optional) If passed, the proof of the authorized ACH agreement terms will be persisted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ach_agreement")]
    public AchAgreement? AchAgreement { get; init; }

    /// <summary>
    /// Enable Communication Delay feature, making sure no communication (email or SMS) is sent to the Customer between 9PM and 8AM in time zone set by the <c>dunning_communication_delay_time_zone</c> attribute.
    /// </summary>
    [JsonPropertyName("dunning_communication_delay_enabled")]
    public bool? DunningCommunicationDelayEnabled { get; init; } = false;

    /// <summary>
    /// Time zone for the Dunning Communication Delay feature.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dunning_communication_delay_time_zone")]
    public string? DunningCommunicationDelayTimeZone { get; init; }

    /// <summary>
    /// Valid only for the Subscription Preview endpoint. When set to <c>true</c> it skips calculating taxes for the current and next billing manifests. Defaults to <c>false</c> when not provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("skip_billing_manifest_taxes")]
    public bool? SkipBillingManifestTaxes { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
