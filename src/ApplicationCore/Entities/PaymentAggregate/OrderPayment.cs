using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment state PayPal owns for one eShop <c>Order</c>: the hold (authorization), the capture, and
/// the refunds — enough for a later request to act on the payment, not only the one that started it.
/// One payment per order (unique index on <see cref="OrderId"/>). Full card details are never stored here.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Owner of the order; every shopper-scoped action checks against this.</summary>
    public string BuyerId { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Unique external invoice id sent to PayPal; the key reconciliation lines up on.</summary>
    public string InvoiceId { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;

    /// <summary>Stable idempotency key for the authorize (create-order) call; reused verbatim on replay.</summary>
    public string AuthorizeRequestId { get; private set; }

    /// <summary>Stable idempotency key for the capture call; reused verbatim on replay.</summary>
    public string CaptureRequestId { get; private set; }

    // ── State PayPal owns ────────────────────────────────────────────────
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Sum of effective refunds against the capture. Never exceeds <see cref="CapturedGross"/>.</summary>
    public decimal RefundedAmount { get; private set; }

    /// <summary>The PayPal event time (authorization/capture create_time); the clock reconciliation filters on.</summary>
    public DateTimeOffset? PayPalCreatedAt { get; private set; }

    /// <summary>Optimistic concurrency token — rejects a second concurrent state transition on SQL Server.</summary>
    public byte[]? RowVersion { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currency, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.Negative(amount, nameof(amount));
        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Amount = amount;
        // Unique per order and unique across process restarts, so PayPal's invoice-id uniqueness holds.
        InvoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}".Substring(0, 27);
        AuthorizeRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus,
        DateTimeOffset? expiresAt, DateTimeOffset? createdAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PayPalCreatedAt = createdAt ?? PayPalCreatedAt ?? DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records a renewed authorization (id + expiry may change) without altering payment status.</summary>
    public void UpdateAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string? captureStatus, decimal? gross, decimal? fee, decimal? net,
        DateTimeOffset? createdAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = gross;
        PaypalFee = fee;
        NetAmount = net;
        PayPalCreatedAt = createdAt ?? PayPalCreatedAt;
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled() => Status = PaymentStatus.Cancelled;

    /// <summary>
    /// The most a further refund may take: the captured gross minus what effective refunds already cover.
    /// </summary>
    public decimal RefundableRemaining =>
        (CapturedGross ?? Amount) - RefundedAmount;

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund StartRefund(string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(Id, idempotencyKey, amount, Currency);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Applies a confirmed refund to the running total and updates the payment status.</summary>
    public void ApplyEffectiveRefund(decimal amount)
    {
        RefundedAmount += amount;
        Status = RefundedAmount >= (CapturedGross ?? Amount)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}
