using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record Subscription
{
    /// <summary>
    /// The subscription unique id within Chargify.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    /// <summary>
    /// The state of a subscription.
    /// * <b>Live States</b>
    ///     * <c>active</c> - A normal, active subscription. It is not in a trial and is paid and up to date.
    ///     * <c>assessing</c> - An internal (transient) state that indicates a subscription is in the middle of periodic assessment. Do not base any access decisions in your app on this state, as it may not always be exposed.
    ///     * <c>pending</c> - An internal (transient) state that indicates a subscription is in the creation process. Do not base any access decisions in your app on this state, as it may not always be exposed.
    ///     * <c>trialing</c> - A subscription in trialing state has a valid trial subscription. This type of subscription may transition to active once payment is received when the trial has ended. Otherwise, it may go to a Problem or End of Life state.
    ///     * <c>paused</c> - An internal state that indicates that your account with Advanced Billing is in arrears.
    /// * <b>Problem States</b>
    ///     * <c>past_due</c> - Indicates that the most recent payment has failed, and payment is past due for this subscription. If you have enabled our automated dunning, this subscription will be in the dunning process (additional status and callbacks from the dunning process will be available in the future). If you are handling dunning and payment updates yourself, you will want to use this state to initiate a payment update from your customers.
    ///     * <c>soft_failure</c> - Indicates that normal assessment/processing of the subscription has failed for a reason that cannot be fixed by the Customer. For example, a Soft Fail may result from a timeout at the gateway or incorrect credentials on your part. The subscriptions should be retried automatically. An interface is being built for you to review problems resulting from these events to take manual action when needed.
    ///     * <c>unpaid</c> - Indicates an unpaid subscription. A subscription is marked unpaid if the retry period expires and you have configured your <see href="https://maxio.zendesk.com/hc/en-us/articles/24287076583565-Dunning-Overview">Dunning</see> settings to have a Final Action of <c>mark the subscription unpaid</c>.
    /// * <b>End of Life States</b>
    ///     * <c>canceled</c> - Indicates a canceled subscription. This may happen at your request (via the API or the web interface) or due to the expiration of the <see href="https://maxio.zendesk.com/hc/en-us/articles/24287076583565-Dunning-Overview">Dunning</see> process without payment. See the <see href="https://maxio.zendesk.com/hc/en-us/articles/24252109503629-Reactivating-and-Resuming">Reactivation</see> documentation for info on how to restart a canceled subscription.
    ///     While a subscription is canceled, its period will not advance, it will not accrue any new charges, and Advanced Billing will not attempt to collect the overdue balance.
    ///     * <c>expired</c> - Indicates a subscription that has expired due to running its normal life cycle. Some products may be configured to have an expiration period. An expired subscription then is one that stayed active until it fulfilled its full period.
    ///     * <c>failed_to_create</c> - Indicates that signup has failed. (You may see this state in a signup_failure webhook.)
    ///     * <c>on_hold</c> - Indicates that a subscription’s billing has been temporarily stopped. While it is expected that the subscription will resume and return to active status, this is still treated as an “End of Life” state because the customer is not paying for services during this time.
    ///     * <c>suspended</c> - Indicates that a prepaid subscription has used up all their prepayment balance. If a prepayment is applied, it will return to an active state.
    ///     * <c>trial_ended</c> - A subscription in a trial_ended state is a subscription that completed a no-obligation trial and did not have a card on file at the expiration of the trial period. See <see href="https://maxio.zendesk.com/hc/en-us/articles/24261076617869-Product-Editing">Product Pricing – No Obligation Trials</see> for more details.
    /// <para>
    /// See <see href="https://maxio.zendesk.com/hc/en-us/articles/24252119027853-Subscription-States">Subscription States</see> for more info about subscription states and state transitions.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("state")]
    public SubscriptionState? State { get; init; }

    /// <summary>
    /// Gives the current outstanding subscription balance in the number of cents.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("balance_in_cents")]
    public long? BalanceInCents { get; init; }

    /// <summary>
    /// Gives the total revenue from the subscription in the number of cents.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_revenue_in_cents")]
    public long? TotalRevenueInCents { get; init; }

    /// <summary>
    /// (Added Nov 5 2013) The recurring amount of the product (and version), currently subscribed. NOTE: this may differ from the current price of the product, if you’ve changed the price of the product but haven’t moved this subscription to a newer version.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; init; }

    /// <summary>
    /// The version of the product for the subscription. Note that this is a deprecated field kept for backwards-compatibility.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_version_number")]
    public int? ProductVersionNumber { get; init; }

    /// <summary>
    /// Timestamp relating to the end of the current (recurring) period (i.e., when the next regularly scheduled attempted charge will occur)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// Timestamp that indicates when capture of payment will be tried or retried. This value will usually track the current_period_ends_at, but will diverge if a renewal payment fails and must be retried. In that case, the current_period_ends_at will advance to the end of the next period (time doesn’t stop because a payment was missed) but the next_assessment_at will be scheduled for the auto-retry time (e.g., 24 hours in the future, in some cases).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    /// <summary>
    /// Timestamp for when the trial period (if any) began
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trial_started_at")]
    public DateTimeOffset? TrialStartedAt { get; init; }

    /// <summary>
    /// Timestamp for when the trial period (if any) ended
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trial_ended_at")]
    public DateTimeOffset? TrialEndedAt { get; init; }

    /// <summary>
    /// Timestamp for when the subscription began (i.e., when it came out of trial, or when it began in the case of no trial)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; init; }

    /// <summary>
    /// Timestamp giving the expiration date of this subscription (if any)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// The creation date for this subscription
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// The date of last update for this subscription
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Seller-provided reason for, or note about, the cancellation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancellation_message")]
    public string? CancellationMessage { get; init; }

    /// <summary>
    /// The process used to cancel the subscription, if the subscription has been canceled. It is nil if the subscription's state is not canceled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancellation_method")]
    public CancellationMethod? CancellationMethod { get; init; }

    /// <summary>
    /// Whether or not the subscription will (or has) canceled at the end of the period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; init; }

    /// <summary>
    /// The timestamp of the most recent cancellation
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>
    /// Timestamp relating to the start of the current (recurring) period
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>
    /// Only valid for webhook payloads The previous state for webhooks that have indicated a change in state. For normal API calls, this will always be the same as the state (current state).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("previous_state")]
    public SubscriptionState? PreviousState { get; init; }

    /// <summary>
    /// The ID of the transaction that generated the revenue
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("signup_payment_id")]
    public int? SignupPaymentId { get; init; }

    /// <summary>
    /// The revenue, formatted as a string of decimal separated dollars and cents, from the subscription signup ($50.00 would be formatted as 50.00)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("signup_revenue")]
    public string? SignupRevenue { get; init; }

    /// <summary>
    /// Timestamp for when the subscription is currently set to cancel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("delayed_cancel_at")]
    public DateTimeOffset? DelayedCancelAt { get; init; }

    /// <summary>
    /// (deprecated) The coupon code of the single coupon currently applied to the subscription. See coupon_codes instead as subscriptions can now have more than one coupon.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon_code")]
    public string? CouponCode { get; init; }

    /// <summary>
    /// A day of month that subscription will be processed on. Can be 1 up to 28 or 'end'.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("snap_day")]
    public string? SnapDay { get; init; }

    /// <summary>
    /// The type of payment collection to be used in the subscription. For legacy Statements Architecture valid options are - <c>invoice</c>, <c>automatic</c>. For current Relationship Invoicing Architecture valid options are - <c>remittance</c>, <c>automatic</c>, <c>prepaid</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_collection_method")]
    public CollectionMethod? PaymentCollectionMethod { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public Customer? Customer { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product")]
    public Product? Product { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("credit_card")]
    public CreditCardPaymentProfile? CreditCard { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("group")]
    public NestedSubscriptionGroup? Group { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_account")]
    public BankAccountPaymentProfile? BankAccount { get; init; }

    /// <summary>
    /// The payment profile type for the active profile on file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_type")]
    public string? PaymentType { get; init; }

    /// <summary>
    /// The subscription's unique code that can be given to referrals.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("referral_code")]
    public string? ReferralCode { get; init; }

    /// <summary>
    /// If a delayed product change is scheduled, the ID of the product that the subscription will be changed to at the next renewal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_product_id")]
    public int? NextProductId { get; init; }

    /// <summary>
    /// If a delayed product change is scheduled, the handle of the product that the subscription will be changed to at the next renewal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_product_handle")]
    public string? NextProductHandle { get; init; }

    /// <summary>
    /// (deprecated) How many times the subscription's single coupon has been used. This field has no replacement for multiple coupons.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon_use_count")]
    public int? CouponUseCount { get; init; }

    /// <summary>
    /// (deprecated) How many times the subscription's single coupon may be used. This field has no replacement for multiple coupons.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon_uses_allowed")]
    public int? CouponUsesAllowed { get; init; }

    /// <summary>
    /// The churn reason code associated to a canceled subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }

    /// <summary>
    /// The date the subscription is scheduled to automatically resume from the on_hold state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("automatically_resume_at")]
    public DateTimeOffset? AutomaticallyResumeAt { get; init; }

    /// <summary>
    /// An array for all the coupons attached to the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon_codes")]
    public IReadOnlyList<string>? CouponCodes { get; init; }

    /// <summary>
    /// The ID of the offer associated with the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("offer_id")]
    public int? OfferId { get; init; }

    /// <summary>
    /// On Relationship Invoicing, the ID of the individual paying for the subscription. Defaults to the Customer ID unless the 'Customer Hierarchies &amp; WhoPays' feature is enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payer_id")]
    public int? PayerId { get; init; }

    /// <summary>
    /// The balance in cents plus the estimated renewal amount in cents. Returned ONLY for the readSubscription operation as it's a compute intensive operation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("current_billing_amount_in_cents")]
    public long? CurrentBillingAmountInCents { get; init; }

    /// <summary>
    /// The product price point currently subscribed to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_id")]
    public int? ProductPricePointId { get; init; }

    /// <summary>
    /// Price point type. We expose the following types:
    /// 1. <b>default</b>: a price point that is marked as a default price for a certain product.
    /// 2. <b>custom</b>: a custom price point.
    /// 3. <b>catalog</b>: a price point that is <b>not</b> marked as a default price for a certain product and is <b>not</b> a custom one.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_type")]
    public PricePointType? ProductPricePointType { get; init; }

    /// <summary>
    /// If a delayed product change is scheduled, the ID of the product price point that the subscription will be changed to at the next renewal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_product_price_point_id")]
    public int? NextProductPricePointId { get; init; }

    /// <summary>
    /// On Relationship Invoicing, the number of days before a renewal invoice is due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("net_terms")]
    public int? NetTerms { get; init; }

    /// <summary>
    /// For European sites subject to PSD2 and using 3D Secure, this can be used to reference a previous transaction for the customer. This will ensure the card will be charged successfully at renewal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stored_credential_transaction_id")]
    public int? StoredCredentialTransactionId { get; init; }

    /// <summary>
    /// The reference value (provided by your app) for the subscription itself.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    /// <summary>
    /// The timestamp of the most recent on hold action.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("on_hold_at")]
    public DateTimeOffset? OnHoldAt { get; init; }

    /// <summary>
    /// Boolean representing whether the subscription is prepaid and currently in dunning. Only returned for Relationship Invoicing sites with the feature enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prepaid_dunning")]
    public bool? PrepaidDunning { get; init; }

    /// <summary>
    /// Additional coupon data. To use this data you also have to include the following param in the request: <c>include[]=coupons</c>.
    /// Only in Read Subscription Endpoint.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupons")]
    public IReadOnlyList<SubscriptionIncludedCoupon>? Coupons { get; init; }

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("receives_invoice_emails")]
    public bool? ReceivesInvoiceEmails { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("locale")]
    public string? Locale { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("scheduled_cancellation_at")]
    public DateTimeOffset? ScheduledCancellationAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("credit_balance_in_cents")]
    public long? CreditBalanceInCents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prepayment_balance_in_cents")]
    public long? PrepaymentBalanceInCents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prepaid_configuration")]
    public PrepaidConfiguration? PrepaidConfiguration { get; init; }

    /// <summary>
    /// Returned only for list/read Subscription operation when <c>include[]=self_service_page_token</c> parameter is provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("self_service_page_token")]
    public string? SelfServicePageToken { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
