using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment/fulfilment state for one <see cref="OrderAggregate.Order"/> (1:1 by <see cref="OrderId"/>).
/// Holds the PayPal-owned ids and statuses (order, authorization, capture, refunds) so a later request can
/// act on the payment, not only the one that started it. Full card details are never stored here.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<OrderRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        InvoiceId = invoiceId;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this payment (JWT identity). Used for shopper-scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to authorize/capture, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>Unique invoice reference sent to PayPal; used to line PayPal transactions up against orders.</summary>
    public string InvoiceId { get; private set; }

    public PaymentStatus Status { get; private set; }

    // PayPal-owned state ------------------------------------------------------------------------

    public string? PayPalOrderId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // Safe card descriptor (never the PAN) ------------------------------------------------------
    public string? CardBrand { get; private set; }
    public string? CardLastDigits { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of refunds that still count (not cancelled/failed).</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.CountsTowardRefunded).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    // Transitions -------------------------------------------------------------------------------

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLastDigits)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLastDigits = cardLastDigits;
        FailureReason = null;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    public void UpdateAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkAuthorizationFailed(string reason)
    {
        FailureReason = reason;
        Status = PaymentStatus.AuthorizationFailed;
        Touch();
    }

    public void MarkFulfilled(string captureId, string? captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Fulfilled;
        Touch();
    }

    public void MarkCanceled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Canceled;
        Touch();
    }

    /// <summary>
    /// Records a refund and moves to <see cref="PaymentStatus.PartiallyRefunded"/> or
    /// <see cref="PaymentStatus.Refunded"/>. Caller must have already validated the cap via
    /// <see cref="RefundableRemaining"/>.
    /// </summary>
    public OrderRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new OrderRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    public bool TryGetRefundByIdempotencyKey(string idempotencyKey, out OrderRefund? refund)
    {
        refund = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        return refund is not null;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
