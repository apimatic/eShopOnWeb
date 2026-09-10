using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The PayPal payment/fulfilment state for an eShop <c>Order</c> (1:1 by <see cref="OrderId"/>). This is
/// the additive money-movement record: it carries the ids and statuses PayPal owns (the hold, the capture,
/// the refunds) so any later request can act on the payment, not only the one that started it. Full card
/// details are never stored here.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

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
        Status = PaymentStatus.PendingPayment;
        // Invoice id PayPal sees: stable for this order (so retries reuse it) but unique across the
        // account — the in-memory order id resets per run, while PayPal remembers invoice ids forever.
        InvoiceId = $"eshop-{orderId}-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }

    /// <summary>The unique invoice id sent to PayPal; reconciliation matches transactions back by it.</summary>
    public string InvoiceId { get; private set; }

    /// <summary>The shopper who owns this payment (the order's buyer). Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total, in <see cref="Currency"/>. The amount authorized and captured.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned state ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }

    /// <summary>RFC-3339 expiry of the current authorization, as PayPal reported it.</summary>
    public string? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }

    // --- Idempotency keys (stable per order operation; reused on retry so a double-click never doubles) ---
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? VoidRequestId { get; private set; }

    /// <summary>Last operator-actionable error, if any (e.g. an authorization that can no longer be renewed).</summary>
    public string? LastError { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Deterministic idempotency key for the authorize step, created once and reused.</summary>
    public string EnsureAuthorizeRequestId()
        => AuthorizeRequestId ??= $"auth-{OrderId}-{Guid.NewGuid():N}";

    public string EnsureCaptureRequestId()
        => CaptureRequestId ??= $"cap-{OrderId}-{Guid.NewGuid():N}";

    public string EnsureVoidRequestId()
        => VoidRequestId ??= $"void-{OrderId}-{Guid.NewGuid():N}";

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        LastError = null;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Updates the held authorization after a re-authorization renewed a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        LastError = null;
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        LastError = null;
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled()
    {
        LastError = null;
        Status = PaymentStatus.Cancelled;
    }

    public void MarkFailed(string error)
    {
        LastError = error;
        Status = PaymentStatus.Failed;
    }

    public void SetOperatorError(string error) => LastError = error;

    /// <summary>The amount still refundable: captured minus already refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>Finds a prior refund made under the same caller idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        RefundedAmount += refund.Amount;
        Status = RefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}
