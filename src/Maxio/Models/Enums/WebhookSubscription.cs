using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<WebhookSubscription>))]
public sealed record WebhookSubscription : StringEnum<WebhookSubscription>
{
    private WebhookSubscription(string value) : base(value)
    {
    }

    public static readonly WebhookSubscription BillingDateChange = new("billing_date_change");

    public static readonly WebhookSubscription ComponentAllocationChange = new("component_allocation_change");

    public static readonly WebhookSubscription ChjsTokenizationFailure = new("chjs_tokenization_failure");

    public static readonly WebhookSubscription ChjsTokenizationSuccess = new("chjs_tokenization_success");

    public static readonly WebhookSubscription CustomerCreate = new("customer_create");

    public static readonly WebhookSubscription CustomerUpdate = new("customer_update");

    public static readonly WebhookSubscription DunningStepReached = new("dunning_step_reached");

    public static readonly WebhookSubscription ExpiringCard = new("expiring_card");

    public static readonly WebhookSubscription ExpirationDateChange = new("expiration_date_change");

    public static readonly WebhookSubscription InvoiceIssued = new("invoice_issued");

    public static readonly WebhookSubscription InvoicePending = new("invoice_pending");

    public static readonly WebhookSubscription MeteredUsage = new("metered_usage");

    public static readonly WebhookSubscription PaymentFailure = new("payment_failure");

    public static readonly WebhookSubscription PaymentSuccess = new("payment_success");

    public static readonly WebhookSubscription DirectDebitPaymentPending = new("direct_debit_payment_pending");

    public static readonly WebhookSubscription DirectDebitPaymentPaidOut = new("direct_debit_payment_paid_out");

    public static readonly WebhookSubscription DirectDebitPaymentRejected = new("direct_debit_payment_rejected");

    public static readonly WebhookSubscription PrepaidSubscriptionBalanceChanged = new("prepaid_subscription_balance_changed");

    public static readonly WebhookSubscription PrepaidUsage = new("prepaid_usage");

    public static readonly WebhookSubscription RefundFailure = new("refund_failure");

    public static readonly WebhookSubscription RefundSuccess = new("refund_success");

    public static readonly WebhookSubscription RenewalFailure = new("renewal_failure");

    public static readonly WebhookSubscription RenewalSuccess = new("renewal_success");

    public static readonly WebhookSubscription SignupFailure = new("signup_failure");

    public static readonly WebhookSubscription SignupSuccess = new("signup_success");

    public static readonly WebhookSubscription StatementClosed = new("statement_closed");

    public static readonly WebhookSubscription StatementSettled = new("statement_settled");

    public static readonly WebhookSubscription SubscriptionCardUpdate = new("subscription_card_update");

    public static readonly WebhookSubscription SubscriptionGroupCardUpdate = new("subscription_group_card_update");

    public static readonly WebhookSubscription SubscriptionProductChange = new("subscription_product_change");

    public static readonly WebhookSubscription SubscriptionProductChangeScheduled = new("subscription_product_change_scheduled");

    public static readonly WebhookSubscription SubscriptionStateChange = new("subscription_state_change");

    public static readonly WebhookSubscription TrialEndNotice = new("trial_end_notice");

    public static readonly WebhookSubscription UpcomingRenewalNotice = new("upcoming_renewal_notice");

    public static readonly WebhookSubscription UpgradeDowngradeFailure = new("upgrade_downgrade_failure");

    public static readonly WebhookSubscription UpgradeDowngradeSuccess = new("upgrade_downgrade_success");

    public static readonly WebhookSubscription PendingCancellationChange = new("pending_cancellation_change");

    public static readonly WebhookSubscription SubscriptionPrepaymentAccountBalanceChanged = new("subscription_prepayment_account_balance_changed");

    public static readonly WebhookSubscription SubscriptionServiceCreditAccountBalanceChanged = new("subscription_service_credit_account_balance_changed");

    public static WebhookSubscription FromValue(string value) => FromValueCore(value);
}
