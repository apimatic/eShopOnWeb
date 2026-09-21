using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal money-movement state for one <see cref="Order"/>. It is a related aggregate rather than a
/// parallel order model: the order is created through the app's existing order/order-item model, and this
/// carries only the payment state PayPal owns (the ids and current status for the hold, the capture and the
/// refunds) so a later request can act on it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = OrderPaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        // Stable idempotency keys so a double-click on pay/fulfil never authorizes or captures twice.
        AuthorizeIdempotencyKey = $"auth-{Guid.NewGuid():N}";
        CaptureIdempotencyKey = $"capture-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Provider { get; private set; } = "PayPal";
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public OrderPaymentStatus Status { get; private set; }

    /// <summary>The external reference we stamp on the PayPal order (invoice_id/custom_id) to reconcile by.</summary>
    public string? OrderReference { get; private set; }

    // Identifiers PayPal owns.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }

    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // Idempotency keys used for the PayPal-Request-Id header on the authorize and capture calls.
    public string AuthorizeIdempotencyKey { get; private set; }
    public string CaptureIdempotencyKey { get; private set; }

    // A safe descriptor of the funding instrument (never full card details).
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total value refunded so far (excludes failed/cancelled refunds).</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.CountsTowardRefundedTotal).Sum(r => r.Amount);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string orderReference,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLast4)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        OrderReference = orderReference;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        Status = OrderPaymentStatus.Authorized;
        FailureReason = null;
        Touch();
    }

    /// <summary>Replace the authorization id/expiry after a re-authorization renewed a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        Status = OrderPaymentStatus.Authorized;
        Touch();
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = OrderPaymentStatus.Captured;
        FailureReason = null;
        Touch();
    }

    public void MarkVoided()
    {
        Status = OrderPaymentStatus.Voided;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        Status = OrderPaymentStatus.Failed;
        Touch();
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// The amount still available to refund. Guards the invariant that a partly-refunded order never
    /// becomes refundable beyond what was captured.
    /// </summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public bool CanRefund(decimal amount) => amount > 0m && amount <= RefundableRemaining;

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        Touch();
    }
}
