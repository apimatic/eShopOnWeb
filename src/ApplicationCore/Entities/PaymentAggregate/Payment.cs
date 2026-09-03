using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment and fulfilment state for an eShop <see cref="OrderAggregate.Order"/>. This is additive:
/// it holds the money-movement state PayPal owns (the hold, the capture, the refunds) so a later request
/// can act on it. It links to the existing order by <see cref="OrderId"/> and does not replace it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = PaymentStatus.PendingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The eShop order this payment belongs to.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this payment (their username / buyer id).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code, from configuration.</summary>
    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned state ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>How the shopper paid: a one-off card, or a saved card id.</summary>
    public string? PaymentMethodDescription { get; private set; }

    // --- Capture financials, as PayPal reported them ---
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public decimal RefundedAmount { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>The amount still available to refund against the capture.</summary>
    public decimal RefundableAmount => (CapturedGross ?? Amount) - RefundedAmount;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus, string? paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        PaymentMethodDescription = paymentMethodDescription;
        Status = PaymentStatus.Authorized;
        FailureReason = null;
        Touch();
    }

    /// <summary>Records a renewed authorization id after a stale one was reauthorized.</summary>
    public void UpdateAuthorization(string authorizationId, string? authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Touch();
    }

    public void MarkCaptured(string captureId, string? captureStatus, decimal capturedGross, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = capturedGross;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkCanceled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Canceled;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>Returns the refund already recorded for an idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (refund.Amount > RefundableAmount)
        {
            throw new InvalidOperationException(
                $"Refund of {refund.Amount} exceeds the refundable amount {RefundableAmount} for payment {Id}.");
        }

        _refunds.Add(refund);
        RefundedAmount += refund.Amount;
        Status = RefundedAmount >= (CapturedGross ?? Amount)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
