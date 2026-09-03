using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record Event
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("key")]
    public required EventKey Key { get; init; }

    [JsonPropertyName("message")]
    [MinLength(1)]
    public required string Message { get; init; }

    [JsonPropertyName("subscription_id")]
    public required int? SubscriptionId { get; init; }

    [JsonPropertyName("customer_id")]
    public required int? CustomerId { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// The schema varies based on the event key. The key-to-event data mapping is as follows:
    /// <para>
    /// * <c>subscription_product_change</c> - SubscriptionProductChange
    /// * <c>subscription_product_change_scheduled</c> - SubscriptionProductChangeScheduled
    /// * <c>subscription_state_change</c> - SubscriptionStateChange
    /// * <c>signup_success</c>, <c>delayed_signup_creation_success</c>, <c>payment_success</c>, <c>payment_failure</c>, <c>renewal_success</c>, <c>renewal_failure</c>, <c>chargeback_lost</c>, <c>chargeback_accepted</c>, <c>chargeback_closed</c> - PaymentRelatedEvents
    /// * <c>refund_success</c> - RefundSuccess
    /// * <c>component_allocation_change</c> - ComponentAllocationChange
    /// * <c>metered_usage</c> - MeteredUsage
    /// * <c>prepaid_usage</c> - PrepaidUsage
    /// * <c>dunning_step_reached</c> - DunningStepReached
    /// * <c>invoice_issued</c> - InvoiceIssued
    /// * <c>pending_cancellation_change</c> - PendingCancellationChange
    /// * <c>prepaid_subscription_balance_changed</c> - PrepaidSubscriptionBalanceChanged
    /// * <c>subscription_group_signup_success</c> and <c>subscription_group_signup_failure</c> - SubscriptionGroupSignupEventData
    /// * <c>proforma_invoice_issued</c> - ProformaInvoiceIssued
    /// * <c>subscription_prepayment_account_balance_changed</c> - PrepaymentAccountBalanceChanged
    /// * <c>payment_collection_method_changed</c> - PaymentCollectionMethodChanged
    /// * <c>subscription_service_credit_account_balance_changed</c> - CreditAccountBalanceChanged
    /// * <c>item_price_point_changed</c> - ItemPricePointChanged
    /// * <c>custom_field_value_change</c> - CustomFieldValueChange
    /// * <c>chjs_tokenization_success</c> - ChjsTokenizationSuccess
    /// * <c>chjs_tokenization_failure</c> - ChjsTokenizationFailure
    /// * The rest, that is <c>delayed_signup_creation_failure</c>, <c>billing_date_change</c>, <c>expiration_date_change</c>, <c>expiring_card</c>,
    /// <c>customer_update</c>, <c>customer_create</c>, <c>customer_delete</c>, <c>upgrade_downgrade_success</c>, <c>upgrade_downgrade_failure</c>,
    /// <c>statement_closed</c>, <c>statement_settled</c>, <c>subscription_card_update</c>, <c>subscription_group_card_update</c>,
    /// <c>subscription_bank_account_update</c>, <c>refund_failure</c>, <c>upcoming_renewal_notice</c>, <c>trial_end_notice</c>,
    /// <c>direct_debit_payment_paid_out</c>, <c>direct_debit_payment_rejected</c>, <c>direct_debit_payment_pending</c>, <c>pending_payment_created</c>,
    /// <c>pending_payment_failed</c>, <c>pending_payment_completed</c>,  don't have event_specific_data defined,
    /// <c>renewal_success_recreated</c>, <c>renewal_failure_recreated</c>, <c>payment_success_recreated</c>, <c>payment_failure_recreated</c>,
    /// <c>subscription_deletion</c>, <c>subscription_group_bank_account_update</c>, <c>subscription_paypal_account_update</c>, <c>subscription_group_paypal_account_update</c>,
    /// <c>subscription_customer_change</c>, <c>account_transaction_changed</c>, <c>go_cardless_payment_paid_out</c>, <c>go_cardless_payment_rejected</c>,
    /// <c>go_cardless_payment_pending</c>, <c>stripe_direct_debit_payment_paid_out</c>, <c>stripe_direct_debit_payment_rejected</c>, <c>stripe_direct_debit_payment_pending</c>,
    /// <c>maxio_payments_direct_debit_payment_paid_out</c>, <c>maxio_payments_direct_debit_payment_rejected</c>, <c>maxio_payments_direct_debit_payment_pending</c>,
    /// <c>invoice_in_collections_canceled</c>, <c>subscription_added_to_group</c>, <c>subscription_removed_from_group</c>, <c>chargeback_opened</c>, <c>chargeback_lost</c>,
    /// <c>chargeback_accepted</c>, <c>chargeback_closed</c>, <c>chargeback_won</c>, <c>payment_collection_method_changed</c>, <c>component_billing_date_changed</c>,
    /// <c>subscription_term_renewal_scheduled</c>, <c>subscription_term_renewal_pending</c>, <c>subscription_term_renewal_activated</c>, <c>subscription_term_renewal_removed</c>
    /// they map to <c>null</c> instead.
    /// </para>
    /// </summary>
    [JsonPropertyName("event_specific_data")]
    public required EventSpecificData? EventSpecificData { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
