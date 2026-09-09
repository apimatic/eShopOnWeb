using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment state that PayPal owns for a single eShop <see cref="OrderAggregate.Order"/>:
/// the hold (authorization), the capture, and the refunds — ids and current status of each — so a
/// later request can act on it, not only the one that started it. This is additive; the existing
/// <see cref="OrderAggregate.Order"/> aggregate is untouched. One payment per order (1:1 by OrderId).
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>The owning shopper — matches <see cref="OrderAggregate.Order.BuyerId"/>. Used for scoping.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize/hold, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// A stable, unique seed for deriving PayPal idempotency keys (PayPal-Request-Id). Fixed for the life
    /// of this payment so retries of the same operation dedupe, but unique per payment so it never collides
    /// with another (including a re-created order after an in-memory restart).
    /// </summary>
    public string IdempotencySeed { get; private set; } = Guid.NewGuid().ToString("N");

    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;

    // --- Hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total amount refunded so far (sum of recorded refunds).</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    /// <summary>Records the money hold created at PayPal (authorization).</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replaces the authorization with a renewed one (after a stale authorization is re-authorized).</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    /// <summary>Records the capture taken at fulfilment, including PayPal's reported fee and net proceeds.</summary>
    public void MarkFulfilled(string captureId, string? captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Fulfilled;
        Touch();
    }

    /// <summary>Records that the hold was released before fulfilment.</summary>
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

    /// <summary>Finds a previously recorded refund for the given idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// The amount that may still be refunded — never more than what was captured, less what has
    /// already been refunded.
    /// </summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public bool CanRefund(decimal amount) =>
        Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded
        && amount > 0m
        && amount <= RefundableRemaining;

    /// <summary>Records a refund and advances the status to partially- or fully-refunded.</summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (refund.Amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {refund.Amount} exceeds the refundable remaining {RefundableRemaining}.");
        }

        _refunds.Add(refund);
        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }
}
