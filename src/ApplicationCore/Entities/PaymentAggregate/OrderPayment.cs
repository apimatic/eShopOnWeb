using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money movement and fulfilment state for a single <see cref="OrderAggregate.Order"/>.
/// Carries the PayPal-owned identifiers and statuses (the hold, the capture, the refunds) so a
/// later request can act on the payment, not only the one that started it. Full card details are
/// never stored here — only PayPal ids and safe amounts.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        InvoiceId = invoiceId;
        Status = OrderPaymentStatus.Pending;
    }

    /// <summary>Owning eShop order id (unique — one payment per order).</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment (the JWT identity name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>Order total captured at pay time; the amount PayPal must hold to the cent.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>External reference sent to PayPal, used to reconcile PayPal and eShop records.</summary>
    public string InvoiceId { get; private set; }

    public OrderPaymentStatus Status { get; private set; }

    // ---- PayPal-owned state ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>PayPal-reported time of the latest transaction (auth/capture); the clock used for reconciliation.</summary>
    public DateTimeOffset? PayPalTransactionTime { get; private set; }

    public decimal RefundedAmount { get; private set; }

    public string? LastError { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, DateTimeOffset? transactionTime)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        if (transactionTime is not null) PayPalTransactionTime = transactionTime;
        Status = OrderPaymentStatus.Authorized;
        LastError = null;
    }

    /// <summary>Records a renewed authorization after the original went stale.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? paypalFee, decimal? netAmount, DateTimeOffset? transactionTime)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        if (transactionTime is not null) PayPalTransactionTime = transactionTime;
        Status = OrderPaymentStatus.Captured;
    }

    public void SetVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = OrderPaymentStatus.Voided;
    }

    public void SetFailed(string error)
    {
        LastError = error;
        Status = OrderPaymentStatus.Failed;
    }

    /// <summary>Clears prior attempt state so a failed/pending payment can be re-attempted cleanly.</summary>
    public void ResetForRetry(string invoiceId)
    {
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        InvoiceId = invoiceId;
        Status = OrderPaymentStatus.Pending;
        PayPalOrderId = null;
        AuthorizationId = null;
        AuthorizationStatus = null;
        AuthorizationExpiresAt = null;
        LastError = null;
    }

    /// <summary>Amount still available to refund without exceeding what was captured.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - ReservedRefundTotal;

    private decimal ReservedRefundTotal => _refunds.Where(r => r.CountsTowardTotal).Sum(r => r.Amount);

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Reserves a refund of <paramref name="amount"/> (defaulting to the full remaining amount) under
    /// the caller's idempotency key. Throws if it would push the total past what was captured.
    /// </summary>
    public PaymentRefund ReserveRefund(string idempotencyKey, decimal? amount)
    {
        var value = amount ?? RefundableRemaining;
        Guard.Against.NegativeOrZero(value, nameof(amount));
        if (value > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {value:0.00} exceeds the remaining refundable amount {RefundableRemaining:0.00} for order {OrderId}.");
        }

        var refund = new PaymentRefund(idempotencyKey, value);
        _refunds.Add(refund);
        return refund;
    }

    public void ApplyRefundSuccess(PaymentRefund refund, string payPalRefundId, string payPalStatus)
    {
        refund.Succeed(payPalRefundId, payPalStatus);
        RecomputeRefundTotals();
    }

    public void ReleaseRefund(PaymentRefund refund)
    {
        refund.Fail();
        RecomputeRefundTotals();
    }

    private void RecomputeRefundTotals()
    {
        RefundedAmount = _refunds.Where(r => r.CountsTowardTotal).Sum(r => r.Amount);
        if (Status is OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            var captured = CapturedAmount ?? Amount;
            Status = RefundedAmount >= captured
                ? OrderPaymentStatus.Refunded
                : RefundedAmount > 0m ? OrderPaymentStatus.PartiallyRefunded : OrderPaymentStatus.Captured;
        }
    }
}
