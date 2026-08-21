using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money movement for a single <see cref="OrderAggregate.Order"/>. Carries enough of the state
/// PayPal owns (ids and current status for the hold, the capture and the refunds) that a later
/// request can act on it, not only the one that started it. This is an additive aggregate — the
/// order itself is unchanged.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode)
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

        // Stable idempotency keys, fixed for the life of the payment, so a double-clicked pay/fulfil
        // reuses the same PayPal-Request-Id and PayPal itself de-duplicates the write.
        CreateRequestId = Guid.NewGuid().ToString();
        AuthorizeRequestId = Guid.NewGuid().ToString();
        CaptureRequestId = Guid.NewGuid().ToString();
    }

    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper who owns the order/payment (the token's user name).</summary>
    public string BuyerId { get; private set; }

    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public PaymentStatus Status { get; private set; }

    // ---- PayPal-owned state ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // ---- Stable idempotency keys (PayPal-Request-Id per logical write) ----
    public string CreateRequestId { get; private set; }
    public string AuthorizeRequestId { get; private set; }
    public string CaptureRequestId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Cumulative amount already refunded against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount that remains refundable — never more than what was captured.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>Records the PayPal order id created for this payment (before authorization).</summary>
    public void RecordPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
    }

    public void MarkAuthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Refreshes the authorization state after a re-authorization renewed a stale hold.</summary>
    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Adds a refund and moves the payment to partially- or fully-refunded accordingly.</summary>
    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = TotalRefunded() >= (CapturedAmount ?? Amount)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }

    /// <summary>Returns an existing refund created under the given idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
