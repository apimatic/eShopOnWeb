using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money side of an <see cref="OrderAggregate.Order"/>. One payment per order. It owns all the
/// PayPal state a later request needs to act on the payment (the hold, the capture, the refunds) and
/// carries stable idempotency keys so a double-click never authorizes/captures/voids twice.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string CurrencyCode { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Stable reference set as PayPal <c>custom_id</c> so reconciliation can line the payment up.</summary>
    public Guid Reference { get; private set; }

    // Stable per-operation idempotency keys (PayPal-Request-Id) — reused across retries of the same op.
    public Guid AuthorizeRequestId { get; private set; }
    public Guid CaptureRequestId { get; private set; }
    public Guid VoidRequestId { get; private set; }

    // PayPal-owned state for the hold.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // PayPal-owned state for the capture.
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, when the shopper paid with a vaulted card.</summary>
    public int? PaymentMethodId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        Reference = Guid.NewGuid();
        AuthorizeRequestId = Guid.NewGuid();
        CaptureRequestId = Guid.NewGuid();
        VoidRequestId = Guid.NewGuid();
    }

    /// <summary>Total refunded so far, excluding refunds PayPal failed/cancelled.</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.CountsTowardRefundedTotal).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded. Never negative.</summary>
    public decimal RefundableRemaining => Math.Max(0m, (CapturedAmount ?? 0m) - TotalRefunded);

    public bool HasRefundWithKey(string idempotencyKey) =>
        _refunds.Any(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public PaymentRefund? GetRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string status, DateTimeOffset? expiresAt, int? paymentMethodId)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodId = paymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records a renewed (reauthorized) hold, replacing the stale authorization id.</summary>
    public void UpdateAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string status, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled(string? authorizationStatus)
    {
        if (authorizationStatus is not null) AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Cancelled;
    }

    public void MarkFailed() => Status = PaymentStatus.Failed;

    /// <summary>
    /// Issues a fresh authorize idempotency key after a declined attempt, so a deliberate retry (e.g.
    /// with a different card) is a new PayPal request rather than an idempotent replay of the decline.
    /// </summary>
    public void RotateAuthorizeRequestId() => AuthorizeRequestId = Guid.NewGuid();

    /// <summary>
    /// Records a refund and moves the payment to <see cref="PaymentStatus.PartiallyRefunded"/> or
    /// <see cref="PaymentStatus.Refunded"/>. Callers must validate the amount against
    /// <see cref="RefundableRemaining"/> first.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, amount, status);
        _refunds.Add(refund);
        Status = RefundableRemaining <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }

    public bool IsAuthorizationStale(DateTimeOffset now) =>
        AuthorizationExpiresAt.HasValue && AuthorizationExpiresAt.Value <= now;
}
