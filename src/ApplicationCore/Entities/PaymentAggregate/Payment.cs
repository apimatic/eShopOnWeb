using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Carries the money-movement and fulfilment state that follows an <see cref="OrderAggregate.Order"/>.
/// One payment exists per order. It records enough of the state PayPal owns (the ids and current
/// status of the hold, the capture, and every refund) that a later request can act on it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this payment; equals the order's <c>BuyerId</c>.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// A globally-unique reference for this payment, used as PayPal's <c>invoice_id</c> and as the
    /// seed for idempotency keys. Stable across pay/capture retries, unique across orders and runs
    /// (the in-memory store resets order ids each run, so the order id alone is not globally unique).
    /// </summary>
    public string Reference { get; private set; } = Guid.NewGuid().ToString("N");

    public PaymentStatus Status { get; private set; } = PaymentStatus.PendingPayment;

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    // ---- PayPal-owned state for the hold (authorization) ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ---- PayPal-owned state for the capture ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, when the shopper paid with a vaulted card (Flow 2).</summary>
    public int? SavedPaymentMethodId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
    }

    /// <summary>Records the PayPal hold (authorization) and moves the payment to Authorized.</summary>
    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the hold with a renewed authorization (after a re-authorization).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture taken at fulfilment and moves the payment to Fulfilled.</summary>
    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Fulfilled;
    }

    /// <summary>Marks the hold released (voided) on a cancel before fulfilment.</summary>
    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
    }

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        RecomputeRefundState();
    }

    private void RecomputeRefundState()
    {
        if (Status != PaymentStatus.Fulfilled && Status != PaymentStatus.PartiallyRefunded && Status != PaymentStatus.Refunded)
        {
            return;
        }

        var refunded = RefundedAmount();
        var captured = CapturedAmount ?? Amount;
        if (refunded <= 0m)
        {
            Status = PaymentStatus.Fulfilled;
        }
        else if (refunded >= captured)
        {
            Status = PaymentStatus.Refunded;
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
    }

    /// <summary>The total already refunded (excludes failed/cancelled refunds).</summary>
    public decimal RefundedAmount() => _refunds.Where(r => r.CountsTowardRefundedTotal).Sum(r => r.Amount);

    /// <summary>What is still available to refund; never exceeds the captured amount.</summary>
    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - RefundedAmount();
}
