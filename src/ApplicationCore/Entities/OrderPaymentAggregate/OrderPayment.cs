using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;

/// <summary>
/// The payment/fulfilment state that follows an <see cref="OrderAggregate.Order"/>. It is a
/// companion aggregate (1:1 with an order via <see cref="OrderId"/>) so the existing Order model
/// is reused, not replaced. It carries enough of the state PayPal owns — the ids and current
/// status of the hold, the capture and each refund — that a later request can act on it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Identity name of the shopper who owns this payment (matches Order.BuyerId).</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>
    /// A stable, unique reference for this payment, generated once at creation. Payment idempotency keys
    /// are derived from it, so a double-click is idempotent within a run while two different orders (even
    /// with the same database id after an in-memory restart) never collide at PayPal.
    /// </summary>
    public string Reference { get; private set; }

    /// <summary>The order total that must be authorized/captured to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- State PayPal owns: the hold ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- State PayPal owns: the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        Reference = Guid.NewGuid().ToString("N");
        Status = PaymentStatus.AwaitingPayment;
    }

    /// <summary>True once a hold exists at PayPal.</summary>
    public bool IsAuthorized => AuthorizationId is not null;

    public bool IsCaptured => CaptureId is not null;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Update the hold after a re-authorization renews a stale authorization.</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string? captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
    }

    public decimal RefundedAmount() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded. Never exceeds the captured amount.</summary>
    public decimal RemainingRefundable() => (CapturedAmount ?? 0m) - RefundedAmount();

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Record a refund and advance the status. Rejects a refund that would take the total refunded
    /// beyond what was captured, so a partly-refunded order never becomes refundable beyond the capture.
    /// </summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));

        if (!IsCaptured)
            throw new InvalidOperationException("Cannot refund a payment that has not been captured.");

        if (refund.Amount <= 0m)
            throw new InvalidOperationException("Refund amount must be greater than zero.");

        if (refund.Amount > RemainingRefundable())
            throw new InvalidOperationException("Refund amount exceeds the remaining refundable balance of the capture.");

        _refunds.Add(refund);

        Status = RemainingRefundable() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}
