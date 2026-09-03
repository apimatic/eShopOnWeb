using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<EventKey>))]
public sealed record EventKey : StringEnum<EventKey>
{
    private EventKey(string value) : base(value)
    {
    }

    public static readonly EventKey PaymentSuccess = new("payment_success");

    public static readonly EventKey PaymentFailure = new("payment_failure");

    public static readonly EventKey SignupSuccess = new("signup_success");

    public static readonly EventKey SignupFailure = new("signup_failure");

    public static readonly EventKey DelayedSignupCreationSuccess = new("delayed_signup_creation_success");

    public static readonly EventKey DelayedSignupCreationFailure = new("delayed_signup_creation_failure");

    public static readonly EventKey BillingDateChange = new("billing_date_change");

    public static readonly EventKey ExpirationDateChange = new("expiration_date_change");

    public static readonly EventKey RenewalSuccess = new("renewal_success");

    public static readonly EventKey RenewalFailure = new("renewal_failure");

    public static readonly EventKey SubscriptionStateChange = new("subscription_state_change");

    public static readonly EventKey SubscriptionProductChange = new("subscription_product_change");

    public static readonly EventKey SubscriptionProductChangeScheduled = new("subscription_product_change_scheduled");

    public static readonly EventKey PendingCancellationChange = new("pending_cancellation_change");

    public static readonly EventKey ExpiringCard = new("expiring_card");

    public static readonly EventKey CustomerUpdate = new("customer_update");

    public static readonly EventKey CustomerCreate = new("customer_create");

    public static readonly EventKey CustomerDelete = new("customer_delete");

    public static readonly EventKey ComponentAllocationChange = new("component_allocation_change");

    public static readonly EventKey MeteredUsage = new("metered_usage");

    public static readonly EventKey PrepaidUsage = new("prepaid_usage");

    public static readonly EventKey UpgradeDowngradeSuccess = new("upgrade_downgrade_success");

    public static readonly EventKey UpgradeDowngradeFailure = new("upgrade_downgrade_failure");

    public static readonly EventKey StatementClosed = new("statement_closed");

    public static readonly EventKey StatementSettled = new("statement_settled");

    public static readonly EventKey SubscriptionCardUpdate = new("subscription_card_update");

    public static readonly EventKey SubscriptionGroupCardUpdate = new("subscription_group_card_update");

    public static readonly EventKey SubscriptionBankAccountUpdate = new("subscription_bank_account_update");

    public static readonly EventKey RefundSuccess = new("refund_success");

    public static readonly EventKey RefundFailure = new("refund_failure");

    public static readonly EventKey UpcomingRenewalNotice = new("upcoming_renewal_notice");

    public static readonly EventKey TrialEndNotice = new("trial_end_notice");

    public static readonly EventKey DunningStepReached = new("dunning_step_reached");

    public static readonly EventKey InvoiceIssued = new("invoice_issued");

    public static readonly EventKey InvoicePending = new("invoice_pending");

    public static readonly EventKey PrepaidSubscriptionBalanceChanged = new("prepaid_subscription_balance_changed");

    public static readonly EventKey SubscriptionGroupSignupSuccess = new("subscription_group_signup_success");

    public static readonly EventKey SubscriptionGroupSignupFailure = new("subscription_group_signup_failure");

    public static readonly EventKey DirectDebitPaymentPaidOut = new("direct_debit_payment_paid_out");

    public static readonly EventKey DirectDebitPaymentRejected = new("direct_debit_payment_rejected");

    public static readonly EventKey DirectDebitPaymentPending = new("direct_debit_payment_pending");

    public static readonly EventKey PendingPaymentCreated = new("pending_payment_created");

    public static readonly EventKey PendingPaymentFailed = new("pending_payment_failed");

    public static readonly EventKey PendingPaymentCompleted = new("pending_payment_completed");

    public static readonly EventKey ProformaInvoiceIssued = new("proforma_invoice_issued");

    public static readonly EventKey SubscriptionPrepaymentAccountBalanceChanged = new("subscription_prepayment_account_balance_changed");

    public static readonly EventKey SubscriptionServiceCreditAccountBalanceChanged = new("subscription_service_credit_account_balance_changed");

    public static readonly EventKey CustomFieldValueChange = new("custom_field_value_change");

    public static readonly EventKey ItemPricePointChanged = new("item_price_point_changed");

    public static readonly EventKey RenewalSuccessRecreated = new("renewal_success_recreated");

    public static readonly EventKey RenewalFailureRecreated = new("renewal_failure_recreated");

    public static readonly EventKey PaymentSuccessRecreated = new("payment_success_recreated");

    public static readonly EventKey PaymentFailureRecreated = new("payment_failure_recreated");

    public static readonly EventKey SubscriptionDeletion = new("subscription_deletion");

    public static readonly EventKey SubscriptionGroupBankAccountUpdate = new("subscription_group_bank_account_update");

    public static readonly EventKey SubscriptionPaypalAccountUpdate = new("subscription_paypal_account_update");

    public static readonly EventKey SubscriptionGroupPaypalAccountUpdate = new("subscription_group_paypal_account_update");

    public static readonly EventKey SubscriptionCustomerChange = new("subscription_customer_change");

    public static readonly EventKey AccountTransactionChanged = new("account_transaction_changed");

    public static readonly EventKey GoCardlessPaymentPaidOut = new("go_cardless_payment_paid_out");

    public static readonly EventKey GoCardlessPaymentRejected = new("go_cardless_payment_rejected");

    public static readonly EventKey GoCardlessPaymentPending = new("go_cardless_payment_pending");

    public static readonly EventKey StripeDirectDebitPaymentPaidOut = new("stripe_direct_debit_payment_paid_out");

    public static readonly EventKey StripeDirectDebitPaymentRejected = new("stripe_direct_debit_payment_rejected");

    public static readonly EventKey StripeDirectDebitPaymentPending = new("stripe_direct_debit_payment_pending");

    public static readonly EventKey MaxioPaymentsDirectDebitPaymentPaidOut = new("maxio_payments_direct_debit_payment_paid_out");

    public static readonly EventKey MaxioPaymentsDirectDebitPaymentRejected = new("maxio_payments_direct_debit_payment_rejected");

    public static readonly EventKey MaxioPaymentsDirectDebitPaymentPending = new("maxio_payments_direct_debit_payment_pending");

    public static readonly EventKey InvoiceInCollectionsCanceled = new("invoice_in_collections_canceled");

    public static readonly EventKey SubscriptionAddedToGroup = new("subscription_added_to_group");

    public static readonly EventKey SubscriptionRemovedFromGroup = new("subscription_removed_from_group");

    public static readonly EventKey ChargebackOpened = new("chargeback_opened");

    public static readonly EventKey ChargebackLost = new("chargeback_lost");

    public static readonly EventKey ChargebackAccepted = new("chargeback_accepted");

    public static readonly EventKey ChargebackClosed = new("chargeback_closed");

    public static readonly EventKey ChargebackWon = new("chargeback_won");

    public static readonly EventKey PaymentCollectionMethodChanged = new("payment_collection_method_changed");

    public static readonly EventKey ComponentBillingDateChanged = new("component_billing_date_changed");

    public static readonly EventKey ChjsTokenizationFailure = new("chjs_tokenization_failure");

    public static readonly EventKey ChjsTokenizationSuccess = new("chjs_tokenization_success");

    public static readonly EventKey SubscriptionTermRenewalScheduled = new("subscription_term_renewal_scheduled");

    public static readonly EventKey SubscriptionTermRenewalPending = new("subscription_term_renewal_pending");

    public static readonly EventKey SubscriptionTermRenewalActivated = new("subscription_term_renewal_activated");

    public static readonly EventKey SubscriptionTermRenewalRemoved = new("subscription_term_renewal_removed");

    public static EventKey FromValue(string value) => FromValueCore(value);
}
