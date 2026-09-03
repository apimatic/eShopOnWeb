using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Extensions;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

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
[JsonConverter(typeof(EventSpecificDataConverter))]
public record EventSpecificData
{
    private readonly Optional<SubscriptionProductChange> _subscriptionProductChangeValue;

    private readonly Optional<SubscriptionProductChangeScheduled> _subscriptionProductChangeScheduledValue;

    private readonly Optional<SubscriptionStateChange> _subscriptionStateChangeValue;

    private readonly Optional<PaymentRelatedEvents> _paymentRelatedEventsValue;

    private readonly Optional<RefundSuccess> _refundSuccessValue;

    private readonly Optional<ComponentAllocationChange> _componentAllocationChangeValue;

    private readonly Optional<MeteredUsage> _meteredUsageValue;

    private readonly Optional<PrepaidUsage> _prepaidUsageValue;

    private readonly Optional<DunningStepReached> _dunningStepReachedValue;

    private readonly Optional<InvoiceIssued> _invoiceIssuedValue;

    private readonly Optional<PendingCancellationChange> _pendingCancellationChangeValue;

    private readonly Optional<PrepaidSubscriptionBalanceChanged> _prepaidSubscriptionBalanceChangedValue;

    private readonly Optional<ProformaInvoiceIssued> _proformaInvoiceIssuedValue;

    private readonly Optional<SubscriptionGroupSignupEventData> _subscriptionGroupSignupEventDataValue;

    private readonly Optional<CreditAccountBalanceChanged> _creditAccountBalanceChangedValue;

    private readonly Optional<PrepaymentAccountBalanceChanged> _prepaymentAccountBalanceChangedValue;

    private readonly Optional<PaymentCollectionMethodChanged> _paymentCollectionMethodChangedValue;

    private readonly Optional<ItemPricePointChanged> _itemPricePointChangedValue;

    private readonly Optional<CustomFieldValueChange> _customFieldValueChangeValue;

    private readonly Optional<ChjsTokenizationSuccess> _chjsTokenizationSuccessValue;

    private readonly Optional<ChjsTokenizationFailure> _chjsTokenizationFailureValue;

    private EventSpecificData(Optional<SubscriptionProductChange> subscriptionProductChangeValue,
        Optional<SubscriptionProductChangeScheduled> subscriptionProductChangeScheduledValue,
        Optional<SubscriptionStateChange> subscriptionStateChangeValue,
        Optional<PaymentRelatedEvents> paymentRelatedEventsValue,
        Optional<RefundSuccess> refundSuccessValue,
        Optional<ComponentAllocationChange> componentAllocationChangeValue,
        Optional<MeteredUsage> meteredUsageValue,
        Optional<PrepaidUsage> prepaidUsageValue,
        Optional<DunningStepReached> dunningStepReachedValue,
        Optional<InvoiceIssued> invoiceIssuedValue,
        Optional<PendingCancellationChange> pendingCancellationChangeValue,
        Optional<PrepaidSubscriptionBalanceChanged> prepaidSubscriptionBalanceChangedValue,
        Optional<ProformaInvoiceIssued> proformaInvoiceIssuedValue,
        Optional<SubscriptionGroupSignupEventData> subscriptionGroupSignupEventDataValue,
        Optional<CreditAccountBalanceChanged> creditAccountBalanceChangedValue,
        Optional<PrepaymentAccountBalanceChanged> prepaymentAccountBalanceChangedValue,
        Optional<PaymentCollectionMethodChanged> paymentCollectionMethodChangedValue,
        Optional<ItemPricePointChanged> itemPricePointChangedValue,
        Optional<CustomFieldValueChange> customFieldValueChangeValue,
        Optional<ChjsTokenizationSuccess> chjsTokenizationSuccessValue,
        Optional<ChjsTokenizationFailure> chjsTokenizationFailureValue)
    {
        _subscriptionProductChangeValue = subscriptionProductChangeValue;
        _subscriptionProductChangeScheduledValue = subscriptionProductChangeScheduledValue;
        _subscriptionStateChangeValue = subscriptionStateChangeValue;
        _paymentRelatedEventsValue = paymentRelatedEventsValue;
        _refundSuccessValue = refundSuccessValue;
        _componentAllocationChangeValue = componentAllocationChangeValue;
        _meteredUsageValue = meteredUsageValue;
        _prepaidUsageValue = prepaidUsageValue;
        _dunningStepReachedValue = dunningStepReachedValue;
        _invoiceIssuedValue = invoiceIssuedValue;
        _pendingCancellationChangeValue = pendingCancellationChangeValue;
        _prepaidSubscriptionBalanceChangedValue = prepaidSubscriptionBalanceChangedValue;
        _proformaInvoiceIssuedValue = proformaInvoiceIssuedValue;
        _subscriptionGroupSignupEventDataValue = subscriptionGroupSignupEventDataValue;
        _creditAccountBalanceChangedValue = creditAccountBalanceChangedValue;
        _prepaymentAccountBalanceChangedValue = prepaymentAccountBalanceChangedValue;
        _paymentCollectionMethodChangedValue = paymentCollectionMethodChangedValue;
        _itemPricePointChangedValue = itemPricePointChangedValue;
        _customFieldValueChangeValue = customFieldValueChangeValue;
        _chjsTokenizationSuccessValue = chjsTokenizationSuccessValue;
        _chjsTokenizationFailureValue = chjsTokenizationFailureValue;
    }

    public static EventSpecificData SubscriptionProductChange(SubscriptionProductChange value) =>
        new(Optional<SubscriptionProductChange>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData SubscriptionProductChangeScheduled(SubscriptionProductChangeScheduled value) =>
        new(default,
            Optional<SubscriptionProductChangeScheduled>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData SubscriptionStateChange(SubscriptionStateChange value) =>
        new(default,
            default,
            Optional<SubscriptionStateChange>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData PaymentRelatedEvents(PaymentRelatedEvents value) =>
        new(default,
            default,
            default,
            Optional<PaymentRelatedEvents>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData RefundSuccess(RefundSuccess value) =>
        new(default,
            default,
            default,
            default,
            Optional<RefundSuccess>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData ComponentAllocationChange(ComponentAllocationChange value) =>
        new(default,
            default,
            default,
            default,
            default,
            Optional<ComponentAllocationChange>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData MeteredUsage(MeteredUsage value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            Optional<MeteredUsage>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData PrepaidUsage(PrepaidUsage value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<PrepaidUsage>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData DunningStepReached(DunningStepReached value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<DunningStepReached>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData InvoiceIssued(InvoiceIssued value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<InvoiceIssued>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData PendingCancellationChange(PendingCancellationChange value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<PendingCancellationChange>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData PrepaidSubscriptionBalanceChanged(PrepaidSubscriptionBalanceChanged value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<PrepaidSubscriptionBalanceChanged>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData ProformaInvoiceIssued(ProformaInvoiceIssued value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<ProformaInvoiceIssued>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData SubscriptionGroupSignupEventData(SubscriptionGroupSignupEventData value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<SubscriptionGroupSignupEventData>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData CreditAccountBalanceChanged(CreditAccountBalanceChanged value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<CreditAccountBalanceChanged>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData PrepaymentAccountBalanceChanged(PrepaymentAccountBalanceChanged value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<PrepaymentAccountBalanceChanged>.Some(value),
            default,
            default,
            default,
            default,
            default);

    public static EventSpecificData PaymentCollectionMethodChanged(PaymentCollectionMethodChanged value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<PaymentCollectionMethodChanged>.Some(value),
            default,
            default,
            default,
            default);

    public static EventSpecificData ItemPricePointChanged(ItemPricePointChanged value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<ItemPricePointChanged>.Some(value),
            default,
            default,
            default);

    public static EventSpecificData CustomFieldValueChange(CustomFieldValueChange value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<CustomFieldValueChange>.Some(value),
            default,
            default);

    public static EventSpecificData ChjsTokenizationSuccess(ChjsTokenizationSuccess value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<ChjsTokenizationSuccess>.Some(value),
            default);

    public static EventSpecificData ChjsTokenizationFailure(ChjsTokenizationFailure value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<ChjsTokenizationFailure>.Some(value));

    public bool TryGetSubscriptionProductChange(out SubscriptionProductChange value) =>
        _subscriptionProductChangeValue.TryGetValue(out value);

    public bool TryGetSubscriptionProductChangeScheduled(out SubscriptionProductChangeScheduled value) =>
        _subscriptionProductChangeScheduledValue.TryGetValue(out value);

    public bool TryGetSubscriptionStateChange(out SubscriptionStateChange value) =>
        _subscriptionStateChangeValue.TryGetValue(out value);

    public bool TryGetPaymentRelatedEvents(out PaymentRelatedEvents value) =>
        _paymentRelatedEventsValue.TryGetValue(out value);

    public bool TryGetRefundSuccess(out RefundSuccess value) => _refundSuccessValue.TryGetValue(out value);

    public bool TryGetComponentAllocationChange(out ComponentAllocationChange value) =>
        _componentAllocationChangeValue.TryGetValue(out value);

    public bool TryGetMeteredUsage(out MeteredUsage value) => _meteredUsageValue.TryGetValue(out value);

    public bool TryGetPrepaidUsage(out PrepaidUsage value) => _prepaidUsageValue.TryGetValue(out value);

    public bool TryGetDunningStepReached(out DunningStepReached value) =>
        _dunningStepReachedValue.TryGetValue(out value);

    public bool TryGetInvoiceIssued(out InvoiceIssued value) => _invoiceIssuedValue.TryGetValue(out value);

    public bool TryGetPendingCancellationChange(out PendingCancellationChange value) =>
        _pendingCancellationChangeValue.TryGetValue(out value);

    public bool TryGetPrepaidSubscriptionBalanceChanged(out PrepaidSubscriptionBalanceChanged value) =>
        _prepaidSubscriptionBalanceChangedValue.TryGetValue(out value);

    public bool TryGetProformaInvoiceIssued(out ProformaInvoiceIssued value) =>
        _proformaInvoiceIssuedValue.TryGetValue(out value);

    public bool TryGetSubscriptionGroupSignupEventData(out SubscriptionGroupSignupEventData value) =>
        _subscriptionGroupSignupEventDataValue.TryGetValue(out value);

    public bool TryGetCreditAccountBalanceChanged(out CreditAccountBalanceChanged value) =>
        _creditAccountBalanceChangedValue.TryGetValue(out value);

    public bool TryGetPrepaymentAccountBalanceChanged(out PrepaymentAccountBalanceChanged value) =>
        _prepaymentAccountBalanceChangedValue.TryGetValue(out value);

    public bool TryGetPaymentCollectionMethodChanged(out PaymentCollectionMethodChanged value) =>
        _paymentCollectionMethodChangedValue.TryGetValue(out value);

    public bool TryGetItemPricePointChanged(out ItemPricePointChanged value) =>
        _itemPricePointChangedValue.TryGetValue(out value);

    public bool TryGetCustomFieldValueChange(out CustomFieldValueChange value) =>
        _customFieldValueChangeValue.TryGetValue(out value);

    public bool TryGetChjsTokenizationSuccess(out ChjsTokenizationSuccess value) =>
        _chjsTokenizationSuccessValue.TryGetValue(out value);

    public bool TryGetChjsTokenizationFailure(out ChjsTokenizationFailure value) =>
        _chjsTokenizationFailureValue.TryGetValue(out value);

    public static implicit operator EventSpecificData(SubscriptionProductChange value) =>
        SubscriptionProductChange(value);

    public static implicit operator EventSpecificData(SubscriptionProductChangeScheduled value) =>
        SubscriptionProductChangeScheduled(value);

    public static implicit operator EventSpecificData(SubscriptionStateChange value) =>
        SubscriptionStateChange(value);

    public static implicit operator EventSpecificData(PaymentRelatedEvents value) =>
        PaymentRelatedEvents(value);

    public static implicit operator EventSpecificData(RefundSuccess value) => RefundSuccess(value);

    public static implicit operator EventSpecificData(ComponentAllocationChange value) =>
        ComponentAllocationChange(value);

    public static implicit operator EventSpecificData(MeteredUsage value) => MeteredUsage(value);

    public static implicit operator EventSpecificData(PrepaidUsage value) => PrepaidUsage(value);

    public static implicit operator EventSpecificData(DunningStepReached value) => DunningStepReached(value);

    public static implicit operator EventSpecificData(InvoiceIssued value) => InvoiceIssued(value);

    public static implicit operator EventSpecificData(PendingCancellationChange value) =>
        PendingCancellationChange(value);

    public static implicit operator EventSpecificData(PrepaidSubscriptionBalanceChanged value) =>
        PrepaidSubscriptionBalanceChanged(value);

    public static implicit operator EventSpecificData(ProformaInvoiceIssued value) =>
        ProformaInvoiceIssued(value);

    public static implicit operator EventSpecificData(SubscriptionGroupSignupEventData value) =>
        SubscriptionGroupSignupEventData(value);

    public static implicit operator EventSpecificData(CreditAccountBalanceChanged value) =>
        CreditAccountBalanceChanged(value);

    public static implicit operator EventSpecificData(PrepaymentAccountBalanceChanged value) =>
        PrepaymentAccountBalanceChanged(value);

    public static implicit operator EventSpecificData(PaymentCollectionMethodChanged value) =>
        PaymentCollectionMethodChanged(value);

    public static implicit operator EventSpecificData(ItemPricePointChanged value) =>
        ItemPricePointChanged(value);

    public static implicit operator EventSpecificData(CustomFieldValueChange value) =>
        CustomFieldValueChange(value);

    public static implicit operator EventSpecificData(ChjsTokenizationSuccess value) =>
        ChjsTokenizationSuccess(value);

    public static implicit operator EventSpecificData(ChjsTokenizationFailure value) =>
        ChjsTokenizationFailure(value);
}

file sealed class EventSpecificDataConverter : JsonConverter<EventSpecificData>
{
    public override EventSpecificData Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<SubscriptionProductChange>(root,
            options,
            out var subscriptionProductChangeValue))
        {
            return EventSpecificData.SubscriptionProductChange(subscriptionProductChangeValue);
        }
        if (JsonSerializer.TryDeserialize<SubscriptionProductChangeScheduled>(root,
            options,
            out var subscriptionProductChangeScheduledValue))
        {
            return EventSpecificData.SubscriptionProductChangeScheduled(subscriptionProductChangeScheduledValue);
        }
        if (JsonSerializer.TryDeserialize<SubscriptionStateChange>(root,
            options,
            out var subscriptionStateChangeValue))
        {
            return EventSpecificData.SubscriptionStateChange(subscriptionStateChangeValue);
        }
        if (JsonSerializer.TryDeserialize<PaymentRelatedEvents>(root, options, out var paymentRelatedEventsValue))
        {
            return EventSpecificData.PaymentRelatedEvents(paymentRelatedEventsValue);
        }
        if (JsonSerializer.TryDeserialize<RefundSuccess>(root, options, out var refundSuccessValue))
        {
            return EventSpecificData.RefundSuccess(refundSuccessValue);
        }
        if (JsonSerializer.TryDeserialize<ComponentAllocationChange>(root,
            options,
            out var componentAllocationChangeValue))
        {
            return EventSpecificData.ComponentAllocationChange(componentAllocationChangeValue);
        }
        if (JsonSerializer.TryDeserialize<MeteredUsage>(root, options, out var meteredUsageValue))
        {
            return EventSpecificData.MeteredUsage(meteredUsageValue);
        }
        if (JsonSerializer.TryDeserialize<PrepaidUsage>(root, options, out var prepaidUsageValue))
        {
            return EventSpecificData.PrepaidUsage(prepaidUsageValue);
        }
        if (JsonSerializer.TryDeserialize<DunningStepReached>(root, options, out var dunningStepReachedValue))
        {
            return EventSpecificData.DunningStepReached(dunningStepReachedValue);
        }
        if (JsonSerializer.TryDeserialize<InvoiceIssued>(root, options, out var invoiceIssuedValue))
        {
            return EventSpecificData.InvoiceIssued(invoiceIssuedValue);
        }
        if (JsonSerializer.TryDeserialize<PendingCancellationChange>(root,
            options,
            out var pendingCancellationChangeValue))
        {
            return EventSpecificData.PendingCancellationChange(pendingCancellationChangeValue);
        }
        if (JsonSerializer.TryDeserialize<PrepaidSubscriptionBalanceChanged>(root,
            options,
            out var prepaidSubscriptionBalanceChangedValue))
        {
            return EventSpecificData.PrepaidSubscriptionBalanceChanged(prepaidSubscriptionBalanceChangedValue);
        }
        if (JsonSerializer.TryDeserialize<ProformaInvoiceIssued>(root, options, out var proformaInvoiceIssuedValue))
        {
            return EventSpecificData.ProformaInvoiceIssued(proformaInvoiceIssuedValue);
        }
        if (JsonSerializer.TryDeserialize<SubscriptionGroupSignupEventData>(root,
            options,
            out var subscriptionGroupSignupEventDataValue))
        {
            return EventSpecificData.SubscriptionGroupSignupEventData(subscriptionGroupSignupEventDataValue);
        }
        if (JsonSerializer.TryDeserialize<CreditAccountBalanceChanged>(root,
            options,
            out var creditAccountBalanceChangedValue))
        {
            return EventSpecificData.CreditAccountBalanceChanged(creditAccountBalanceChangedValue);
        }
        if (JsonSerializer.TryDeserialize<PrepaymentAccountBalanceChanged>(root,
            options,
            out var prepaymentAccountBalanceChangedValue))
        {
            return EventSpecificData.PrepaymentAccountBalanceChanged(prepaymentAccountBalanceChangedValue);
        }
        if (JsonSerializer.TryDeserialize<PaymentCollectionMethodChanged>(root,
            options,
            out var paymentCollectionMethodChangedValue))
        {
            return EventSpecificData.PaymentCollectionMethodChanged(paymentCollectionMethodChangedValue);
        }
        if (JsonSerializer.TryDeserialize<ItemPricePointChanged>(root, options, out var itemPricePointChangedValue))
        {
            return EventSpecificData.ItemPricePointChanged(itemPricePointChangedValue);
        }
        if (JsonSerializer.TryDeserialize<CustomFieldValueChange>(root,
            options,
            out var customFieldValueChangeValue))
        {
            return EventSpecificData.CustomFieldValueChange(customFieldValueChangeValue);
        }
        if (JsonSerializer.TryDeserialize<ChjsTokenizationSuccess>(root,
            options,
            out var chjsTokenizationSuccessValue))
        {
            return EventSpecificData.ChjsTokenizationSuccess(chjsTokenizationSuccessValue);
        }
        if (JsonSerializer.TryDeserialize<ChjsTokenizationFailure>(root,
            options,
            out var chjsTokenizationFailureValue))
        {
            return EventSpecificData.ChjsTokenizationFailure(chjsTokenizationFailureValue);
        }
        throw new JsonException($"JSON does not match SubscriptionProductChange or SubscriptionProductChangeScheduled or SubscriptionStateChange or PaymentRelatedEvents or RefundSuccess or ComponentAllocationChange or MeteredUsage or PrepaidUsage or DunningStepReached or InvoiceIssued or PendingCancellationChange or PrepaidSubscriptionBalanceChanged or ProformaInvoiceIssued or SubscriptionGroupSignupEventData or CreditAccountBalanceChanged or PrepaymentAccountBalanceChanged or PaymentCollectionMethodChanged or ItemPricePointChanged or CustomFieldValueChange or ChjsTokenizationSuccess or ChjsTokenizationFailure schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, EventSpecificData value, JsonSerializerOptions options)
    {
        if (value.TryGetSubscriptionProductChange(out var subscriptionProductChangeValue))
        {
            JsonSerializer.Serialize(writer, subscriptionProductChangeValue, options);
        }
        else if (value.TryGetSubscriptionProductChangeScheduled(out var subscriptionProductChangeScheduledValue))
        {
            JsonSerializer.Serialize(writer, subscriptionProductChangeScheduledValue, options);
        }
        else if (value.TryGetSubscriptionStateChange(out var subscriptionStateChangeValue))
        {
            JsonSerializer.Serialize(writer, subscriptionStateChangeValue, options);
        }
        else if (value.TryGetPaymentRelatedEvents(out var paymentRelatedEventsValue))
        {
            JsonSerializer.Serialize(writer, paymentRelatedEventsValue, options);
        }
        else if (value.TryGetRefundSuccess(out var refundSuccessValue))
        {
            JsonSerializer.Serialize(writer, refundSuccessValue, options);
        }
        else if (value.TryGetComponentAllocationChange(out var componentAllocationChangeValue))
        {
            JsonSerializer.Serialize(writer, componentAllocationChangeValue, options);
        }
        else if (value.TryGetMeteredUsage(out var meteredUsageValue))
        {
            JsonSerializer.Serialize(writer, meteredUsageValue, options);
        }
        else if (value.TryGetPrepaidUsage(out var prepaidUsageValue))
        {
            JsonSerializer.Serialize(writer, prepaidUsageValue, options);
        }
        else if (value.TryGetDunningStepReached(out var dunningStepReachedValue))
        {
            JsonSerializer.Serialize(writer, dunningStepReachedValue, options);
        }
        else if (value.TryGetInvoiceIssued(out var invoiceIssuedValue))
        {
            JsonSerializer.Serialize(writer, invoiceIssuedValue, options);
        }
        else if (value.TryGetPendingCancellationChange(out var pendingCancellationChangeValue))
        {
            JsonSerializer.Serialize(writer, pendingCancellationChangeValue, options);
        }
        else if (value.TryGetPrepaidSubscriptionBalanceChanged(out var prepaidSubscriptionBalanceChangedValue))
        {
            JsonSerializer.Serialize(writer, prepaidSubscriptionBalanceChangedValue, options);
        }
        else if (value.TryGetProformaInvoiceIssued(out var proformaInvoiceIssuedValue))
        {
            JsonSerializer.Serialize(writer, proformaInvoiceIssuedValue, options);
        }
        else if (value.TryGetSubscriptionGroupSignupEventData(out var subscriptionGroupSignupEventDataValue))
        {
            JsonSerializer.Serialize(writer, subscriptionGroupSignupEventDataValue, options);
        }
        else if (value.TryGetCreditAccountBalanceChanged(out var creditAccountBalanceChangedValue))
        {
            JsonSerializer.Serialize(writer, creditAccountBalanceChangedValue, options);
        }
        else if (value.TryGetPrepaymentAccountBalanceChanged(out var prepaymentAccountBalanceChangedValue))
        {
            JsonSerializer.Serialize(writer, prepaymentAccountBalanceChangedValue, options);
        }
        else if (value.TryGetPaymentCollectionMethodChanged(out var paymentCollectionMethodChangedValue))
        {
            JsonSerializer.Serialize(writer, paymentCollectionMethodChangedValue, options);
        }
        else if (value.TryGetItemPricePointChanged(out var itemPricePointChangedValue))
        {
            JsonSerializer.Serialize(writer, itemPricePointChangedValue, options);
        }
        else if (value.TryGetCustomFieldValueChange(out var customFieldValueChangeValue))
        {
            JsonSerializer.Serialize(writer, customFieldValueChangeValue, options);
        }
        else if (value.TryGetChjsTokenizationSuccess(out var chjsTokenizationSuccessValue))
        {
            JsonSerializer.Serialize(writer, chjsTokenizationSuccessValue, options);
        }
        else if (value.TryGetChjsTokenizationFailure(out var chjsTokenizationFailureValue))
        {
            JsonSerializer.Serialize(writer, chjsTokenizationFailureValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(EventSpecificData)} contains no valid value to serialize.");
        }
    }
}
