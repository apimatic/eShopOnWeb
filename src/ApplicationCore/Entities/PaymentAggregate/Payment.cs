using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement record for a single <see cref="OrderAggregate.Order"/>. It carries the state
/// PayPal owns (the authorization hold, the capture, and the refunds) so that a later request can act
/// on the payment, not only the one that created it. This is additive: the existing order/order-item
/// model is untouched and a Payment simply references its <see cref="OrderId"/>.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Owning shopper (order's BuyerId), duplicated here for ownership scoping.</summary>
    public string BuyerId { get; private set; }

    public string Currency { get; private set; }

    /// <summary>Order total the shopper must pay, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;

    // --- PayPal-owned state -------------------------------------------------
    public string? PayPalOrderId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Safe descriptor of the instrument used (never full card details) ---
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    /// <summary>Number of authorization attempts; used to derive fresh idempotency keys after a failure.</summary>
    public int AuthorizationAttempts { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currency, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.Negative(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Amount = amount;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    public int NextAuthorizationAttempt()
    {
        AuthorizationAttempts++;
        Touch();
        return AuthorizationAttempts;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLast4)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        FailureReason = null;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replaces the authorization id/expiry after a stale hold is renewed (reauthorize).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkCancelled()
    {
        AuthorizationStatus = AuthorizationId is null ? AuthorizationStatus : "VOIDED";
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        Status = PaymentStatus.Failed;
        Touch();
    }

    public decimal RefundedAmount() => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable() => (CapturedAmount ?? 0m) - RefundedAmount();

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);

        // Never let the order become refundable beyond what was captured.
        var captured = CapturedAmount ?? 0m;
        Status = RefundedAmount() >= captured ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
    }
}
