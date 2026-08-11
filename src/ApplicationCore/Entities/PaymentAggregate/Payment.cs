using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for a single <see cref="OrderAggregate.Order"/>. It is an additive
/// aggregate: the order/order-item model is untouched, and this carries all of the
/// money-movement and fulfilment state, including the ids and statuses PayPal owns
/// (the hold, the capture, and each refund) so a later request can act on them.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        Reference = Guid.NewGuid();
    }

    /// <summary>
    /// A globally-unique reference for this payment instance. Idempotency keys are derived
    /// from it rather than from the order id, so keys never collide with PayPal's persisted
    /// idempotency records across runs — important because the in-memory store resets order
    /// ids to 1 on every restart.
    /// </summary>
    public Guid Reference { get; private set; }

    /// <summary>How many times an authorization has been attempted. Bumps the authorize key so a genuine decline can be retried.</summary>
    public int AuthorizeAttempts { get; private set; }

    /// <summary>The PayPal-Request-Id for the current authorize attempt (stable across a double-click).</summary>
    public string AuthorizeIdempotencyKey => $"auth-{Reference:N}-{AuthorizeAttempts}";

    /// <summary>The PayPal-Request-Id for the capture (stable across a double-click).</summary>
    public string CaptureIdempotencyKey => $"cap-{Reference:N}";

    /// <summary>The eShop order this payment belongs to.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order (used to scope shopper actions).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to hold and, ultimately, to capture.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // --- State owned by PayPal: the hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    // --- State owned by PayPal: the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total value returned to the shopper that still counts against the capture.</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>How much of the captured payment is still available to refund.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    // --- Transitions ---

    /// <summary>Bumps the attempt count so the next authorize uses a fresh idempotency key.</summary>
    public void RecordAuthorizeFailure() => AuthorizeAttempts++;

    public void SetAuthorization(string payPalOrderId, string authorizationId, string status)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>A stale authorization was renewed before fulfilment; the hold id may change.</summary>
    public void RenewAuthorization(string authorizationId, string status)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void SetCapture(string captureId, string status, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled()
    {
        if (AuthorizationId is not null)
        {
            AuthorizationStatus = "VOIDED";
        }
        Status = PaymentStatus.Cancelled;
    }

    /// <summary>Records a refund and rolls the payment to partially- or fully-refunded.</summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
