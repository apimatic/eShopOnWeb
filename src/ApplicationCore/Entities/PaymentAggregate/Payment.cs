using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for a single <see cref="OrderAggregate.Order"/>. It owns the state that lives
/// on PayPal's side — the hold (authorization), the capture and the refunds — so that a later
/// request (fulfil, cancel, refund) can act on the payment without re-deriving it from the
/// call that started it. One payment maps one-to-one to one order.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

    private Payment() { }

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
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        IdempotencySeed = Guid.NewGuid();
    }

    public int OrderId { get; private set; }

    /// <summary>When the payment record was created (≈ when the order was placed).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// A globally-unique seed for this payment, used to build PayPal idempotency keys that stay
    /// stable across retries yet never collide with another payment (order ids alone would clash
    /// across in-memory restarts and reused sandbox accounts).
    /// </summary>
    public Guid IdempotencySeed { get; private set; }

    /// <summary>Owner of the payment (username/email), used to scope access to the caller.</summary>
    public string BuyerId { get; private set; } = default!;

    /// <summary>The order total that must be authorized, to the cent.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; } = default!;

    public PaymentStatus Status { get; private set; }

    // ---- Authorization (the hold) ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ---- Capture (money taken at fulfilment) ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, if any (null for one-off card payments).</summary>
    public int? SavedPaymentMethodId { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of refunds that PayPal has not failed/cancelled — i.e. money actually given back or in flight.</summary>
    public decimal TotalRefunded => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkAuthorized(string paypalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records a renewed (reauthorized) hold that replaces a stale one before capture.</summary>
    public void MarkReauthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
    }

    /// <summary>
    /// Registers a refund against the capture. Guards that the sum of refunds never exceeds
    /// the captured amount, and advances the status to partially/fully refunded.
    /// </summary>
    public PaymentRefund AddRefund(string paypalRefundId, decimal amount, string idempotencyKey, string status)
    {
        var refund = new PaymentRefund(paypalRefundId, amount, idempotencyKey, status);
        _refunds.Add(refund);

        // Treat amounts within a cent of the captured total as a full refund.
        if (TotalRefunded >= (CapturedAmount ?? 0m) - 0.005m)
        {
            Status = PaymentStatus.Refunded;
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
        }

        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
