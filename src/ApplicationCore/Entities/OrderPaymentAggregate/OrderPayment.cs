using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;

/// <summary>
/// The payment attached to an <see cref="OrderAggregate.Order"/>. One payment per order.
/// It carries the money-movement state that PayPal owns — the ids and current status of the
/// hold (authorization), the capture and the refunds — so a later request (fulfil, cancel,
/// refund, reconcile) can act on it rather than only the request that started it.
/// This is an additive aggregate; the existing Order/OrderItem model is untouched.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Owner of the payment (the shopper's identity). Denormalised from the order so
    /// payment access can be scoped to the caller without a join.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>Order total, the amount to hold and then take, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Stable reference sent to PayPal (invoice_id / custom_id) so a reporting
    /// transaction can be lined back up against this order during reconciliation.</summary>
    public string Reference { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.PendingPayment;

    // --- PayPal-owned state: the hold ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- PayPal-owned state: the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Safe descriptor of the card used to pay (never full card details) ---
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode, string reference)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(reference, nameof(reference));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Reference = reference;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    /// <summary>Records the hold created at pay time.</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLast4)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replaces the hold with a renewed one (reauthorize returns a new authorization id).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    /// <summary>Records the money actually taken at fulfilment, with what PayPal reported.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    /// <summary>Marks the hold released before capture. No money ever moved.</summary>
    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture is still refundable. Never exceeds what was captured.</summary>
    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - TotalRefunded();

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Adds a refund and advances the status. Guards that a partly-refunded order never
    /// becomes refundable beyond what was captured.
    /// </summary>
    public void AddRefund(Refund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }
        if (refund.Amount > RefundableAmount())
        {
            throw new InvalidOperationException(
                $"Refund of {refund.Amount} exceeds the remaining refundable amount {RefundableAmount()}.");
        }

        _refunds.Add(refund);
        Status = RefundableAmount() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
    }
}
