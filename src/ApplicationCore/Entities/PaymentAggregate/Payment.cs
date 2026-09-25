using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Carries the money-movement and fulfilment state for a single <see
/// cref="Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order"/> (1:1 by
/// <see cref="OrderId"/>), plus the identifiers and statuses PayPal owns (the hold, the capture,
/// the refunds). The existing order/order-item model is reused unchanged for items, total and
/// buyer; this aggregate is the additive layer that lets an order actually collect money.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    /// <summary>The eShop order this payment settles.</summary>
    public int OrderId { get; private set; }

    /// <summary>The buyer (email/username) who owns this payment. Used for shopper scoping.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The authoritative amount to collect, from catalog prices (order total).</summary>
    public decimal Amount { get; private set; }

    /// <summary>Unique per-order reference sent to PayPal (invoice_id) for reconciliation.</summary>
    public string InvoiceId { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;

    // ---- State PayPal owns ----

    /// <summary>PayPal's order id created at authorization time.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal's authorization (hold) id.</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's current status for the authorization (e.g. CREATED, VOIDED).</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization hold expires (used to detect a stale hold).</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal's capture id (created at fulfilment).</summary>
    public string? CaptureId { get; private set; }

    /// <summary>PayPal's current status for the capture (e.g. COMPLETED, PARTIALLY_REFUNDED).</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>The captured gross amount, as PayPal reported it.</summary>
    public decimal? CapturedGross { get; private set; }

    /// <summary>PayPal's fee on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>The net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>A safe descriptor of the instrument used (e.g. "Visa ****1111"). Never card details.</summary>
    public string? InstrumentDescriptor { get; private set; }

    /// <summary>Cumulative amount refunded across all completed refunds.</summary>
    public decimal RefundedAmount { get; private set; }

    /// <summary>A human-readable reason when the payment fails or an action cannot proceed.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedDate { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        InvoiceId = invoiceId;
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;

    /// <summary>Record the PayPal order id as soon as it is known (before/independent of the hold).</summary>
    public void SetPayPalOrderId(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        Touch();
    }

    /// <summary>Money is held. Transition to <see cref="PaymentStatus.Authorized"/>.</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? instrumentDescriptor)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        InstrumentDescriptor = instrumentDescriptor;
        FailureReason = null;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>A renewed hold (reauthorization) replaced the previous one.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    /// <summary>The authorized funds were captured at fulfilment. Money taken.</summary>
    public void MarkFulfilled(string captureId, string captureStatus, decimal capturedGross,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = capturedGross;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Fulfilled;
        Touch();
    }

    /// <summary>The hold was released before fulfilment. No money moved.</summary>
    public void MarkCancelled(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>
    /// Whether an additional refund of <paramref name="amount"/> is allowed: only a fulfilled or
    /// partly-refunded payment can be refunded, and never beyond what was captured.
    /// </summary>
    public bool CanRefund(decimal amount)
    {
        if (Status != PaymentStatus.Fulfilled && Status != PaymentStatus.PartiallyRefunded)
        {
            return false;
        }
        if (!CapturedGross.HasValue || amount <= 0m)
        {
            return false;
        }
        return RefundedAmount + amount <= CapturedGross.Value;
    }

    /// <summary>The remaining amount that may still be refunded.</summary>
    public decimal RefundableRemaining =>
        (CapturedGross ?? 0m) - RefundedAmount;

    /// <summary>Find an already-recorded refund for a caller idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.Find(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Record a completed/pending refund and advance the payment's refund state.</summary>
    public void RecordRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        RefundedAmount += refund.Amount;

        if (CapturedGross.HasValue && RefundedAmount >= CapturedGross.Value)
        {
            Status = PaymentStatus.Refunded;
            CaptureStatus = "REFUNDED";
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
            CaptureStatus = "PARTIALLY_REFUNDED";
        }
        Touch();
    }
}
